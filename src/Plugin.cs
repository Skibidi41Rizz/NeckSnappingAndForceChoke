using System.Collections.Generic;
using Il2Cpp;
using Il2CppActiveRagdoll;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Shared;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BrutalistickChoke.Plugin), "Neck Snapping and Force Choke", "1.0.0", "misha")]
[assembly: MelonGame("Kubunautilus", "BrutalistickVR")]

namespace BrutalistickChoke
{
    /// Удушение: пока игрок держит НПС за шею, НПС теряет равновесие, дёргает
    /// ногами, хрипит и получает урон. B (Secondary) ломает шею штатной системой
    /// переломов.
    ///
    /// Проверено диагностикой: захват Neck дает ColliderBridge.damageRegion == "neck"
    /// и StickJointBreaker(idx=1, region="neck") прямо на этом объекте. Ванильная игра
    /// удушение не наносит, поэтому урон и звук - на моде.
    public class Plugin : MelonMod
    {
        const string Tag = "Choke";

        const float DamagePerSecond = 12f;
        const float TickSeconds = 0.5f;
        const float LogInterval = 2f;

        // Волна реакции "бьется" - родной метод игры. Сама игра зовет его только
        // когда НПС на весу или умирает, поэтому пульсируем вручную.
        const float ConvulseInterval = 0.7f;

        // Потеря равновесия: НПС не может стоять, пока его душат.
        const bool Unbalance = true;
        const float UnbalanceInterval = 1.2f;

        // Физическая дрожь тела.
        const bool Twitch = true;
        const float TwitchInterval = 0.12f;
        const float TwitchStrength = 2.2f;

        // Судорога ног: гнём колени через цель привода сустава, а не толчком -
        // от толчка ноги улетают в сторону, а тут именно сгиб-разгиб.
        const float LegBendPeriod = 1.1f;   // секунд на полный цикл
        const float LegBendAngle = 45f;     // градусы сгиба

        // НПС держится на ногах родной ConstantForce на поясе. Гасим его, пока душат.
        const bool KillPelvisForce = true;

        // Хрип/последний выдох. Частота - чтобы звук шел все удушение, а не только в конце.
        const float BreathInterval = 1.3f;

        const string NeckRegion = "neck";

        class Choke
        {
            public StickHealth health;
            public ColliderBridge bridge;
            public Rigidbody[] twitch;
            public ConfigurableJoint[] knees;
            public Quaternion[] kneeBase;
            public Vector3[] kneeAxis;
            public ActRagJoint[] kneeScripts;
            public bool[] kneeScriptWas;
            public ConstantForce pelvis;
            public bool pelvisWasEnabled;
            public bool wasChoking;
            public float nextTick;
            public float nextBreath;
            public float nextLog;
            public float nextConvulse;
            public float nextTwitch;
            public float nextUnbalance;
        }

        readonly List<HVRHandGrabber> hands = new List<HVRHandGrabber>();
        readonly Dictionary<int, Choke> active = new Dictionary<int, Choke>();
        readonly HashSet<int> stillHeld = new HashSet<int>();
        float nextScan;
        bool secondaryWasDown;

        public override void OnInitializeMelon()
        {
            MelonLogger.Msg($"{Tag}: loaded, {DamagePerSecond} dmg/s, B breaks the neck");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            foreach (var pair in active)
                EndChoke(pair.Value);
            hands.Clear();
            active.Clear();
            stillHeld.Clear();
            nextScan = 0f;
        }

        public override void OnUpdate()
        {
            EnsureHands();

            stillHeld.Clear();
            for (int i = 0; i < hands.Count; i++)
            {
                var h = hands[i];
                if (h == null)
                    continue;

                var obj = h.IsGrabbing ? h.HeldObject : null;
                if (obj == null)
                    continue;

                var bridge = obj.GetComponentInParent<ColliderBridge>();
                if (bridge == null || !IsNeck(bridge.damageRegion))
                    continue;

                var health = bridge.stickHealth;
                if (health == null)
                    health = obj.GetComponentInParent<StickHealth>();
                if (health == null || health.isDead)
                    continue;

                int id = health.GetInstanceID();
                Choke c;
                if (!active.TryGetValue(id, out c))
                {
                    c = new Choke { health = health, bridge = bridge };
                    c.twitch = FindTwitchBodies(health);
                    FindKnees(c);
                    active[id] = c;
                    BeginChoke(c);
                }
                stillHeld.Add(id);
                Tick(c);
            }

            DropReleased();
            CheckNeckSnap();
        }

        void BeginChoke(Choke c)
        {
            var h = c.health;
            c.wasChoking = h.isChoking;
            // isChoking в игре никто не выставляет, а звук и, возможно, челюсть
            // читают именно его - держим true на все удержание.
            h.isChoking = true;
            c.pelvis = h.pelvisForce;
            if (c.pelvis != null)
                c.pelvisWasEnabled = c.pelvis.enabled;

            // ActRagJoint каждый кадр перекладывает позу скелета в цель сустава и
            // перебивает наши записи - на время удушения забираем колени себе.
            for (int i = 0; i < c.kneeScripts.Length; i++)
            {
                var aj = c.kneeScripts[i];
                if (aj == null)
                    continue;
                c.kneeScriptWas[i] = aj.enabled;
                aj.enabled = false;
            }
            int spring = c.knees.Length > 0 ? Mathf.RoundToInt(c.knees[0].slerpDrive.positionSpring) : 0;
            MelonLogger.Msg($"{Tag}: choking '{h.name}', hp={h.StickCurrentHealth:F1}, twitchRbs={c.twitch.Length}, kneeJoints={c.knees.Length}, kneeSpring={spring}, pelvisForce={(c.pelvis != null)}");
        }

        void EndChoke(Choke c)
        {
            var h = c.health;
            MelonLogger.Msg($"{Tag}: stop, hp={(h == null ? 0f : h.StickCurrentHealth):F1}");
            if (h == null)
                return;
            h.isChoking = c.wasChoking;
            if (c.pelvis != null)
                c.pelvis.enabled = c.pelvisWasEnabled;
            for (int i = 0; i < c.knees.Length; i++)
            {
                if (c.knees[i] != null)
                    c.knees[i].targetRotation = c.kneeBase[i];
                var aj = c.kneeScripts[i];
                if (aj != null)
                    aj.enabled = c.kneeScriptWas[i];
            }
        }

        void Tick(Choke c)
        {
            float now = Time.time;

            if (now >= c.nextTick)
            {
                c.nextTick = now + TickSeconds;
                c.health.ReceivedGeneralDamage(
                    DamagePerSecond * TickSeconds, StickHealth.DamageType.Choking, NeckRegion);
            }

            if (now >= c.nextConvulse)
            {
                c.nextConvulse = now + ConvulseInterval;
                c.health.StartFlailingOrProtecting();
            }

            // Гасим родную силу пояса каждый кадр: если игра перезапишет поле,
            // выключенный компонент все равно не даст НПС встать.
            if (KillPelvisForce && c.pelvis != null)
                c.pelvis.enabled = false;

            if (Unbalance && now >= c.nextUnbalance)
            {
                c.nextUnbalance = now + UnbalanceInterval;
                c.health.ForceUnbalance();
            }

            if (Twitch && now >= c.nextTwitch)
            {
                c.nextTwitch = now + TwitchInterval;
                Spin(c.twitch, TwitchStrength);
            }

            if (now >= c.nextBreath)
            {
                c.nextBreath = now + BreathInterval;
                Breath(c.health.enemyVoiceManager);
            }

            if (now >= c.nextLog)
            {
                c.nextLog = now + LogInterval;
                MelonLogger.Msg($"{Tag}: hp={c.health.StickCurrentHealth:F1} dead={c.health.isDead}");
            }
        }

        // Гнём колени синусом. Пишем цель привода в трёх точках кадра, потому что
        // ActRagJoint мог обновить её сам; последняя запись перед шагом физики - наша.
        public override void OnLateUpdate() => ApplyBend();

        public override void OnFixedUpdate() => ApplyBend();

        void ApplyBend()
        {
            if (active.Count == 0)
                return;

            float k = 0.5f - 0.5f * Mathf.Cos(Time.time * Mathf.PI * 2f / LegBendPeriod);
            float angle = LegBendAngle * k;
            foreach (var pair in active)
            {
                var c = pair.Value;
                for (int i = 0; i < c.knees.Length; i++)
                {
                    var joint = c.knees[i];
                    if (joint == null)
                        continue;
                    joint.targetRotation = c.kneeBase[i] * Quaternion.AngleAxis(angle, c.kneeAxis[i]);
                }
            }
        }

        static void Spin(Rigidbody[] bodies, float strength)
        {
            for (int i = 0; i < bodies.Length; i++)
            {
                var rb = bodies[i];
                if (rb == null)
                    continue;
                var spin = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f));
                rb.AddTorque(spin * strength, ForceMode.VelocityChange);
            }
        }

        // Хрип: сначала родной путь (он же двигает челюсть), если тот молчит -
        // играем клипы банка напрямую, чтобы выдох шел все удушение.
        static void Breath(EnemyVoiceManager voice)
        {
            if (voice == null)
                return;
            var src = voice.voiceSource;
            if (src != null && src.isPlaying)
                return;

            voice.PlayChokingSound();
            if (src != null && src.isPlaying)
                return;

            var bank = voice.activeSoundBank;
            if (bank == null)
                return;
            var clips = bank.chokingSounds;
            if (clips == null || clips.Length == 0)
                clips = bank.upperChestTraumaDeath;
            if (clips == null || clips.Length == 0)
                clips = bank.slitThroatDeath;
            if (clips == null || clips.Length == 0)
                clips = bank.majorHurt;
            if (clips != null && clips.Length > 0)
                voice.PlayRandomClip(clips, false);
        }

        void DropReleased()
        {
            if (active.Count == 0)
                return;

            List<int> gone = null;
            foreach (var pair in active)
            {
                if (stillHeld.Contains(pair.Key) && !pair.Value.health.isDead)
                    continue;
                if (gone == null)
                    gone = new List<int>();
                gone.Add(pair.Key);
            }
            if (gone == null)
                return;

            for (int i = 0; i < gone.Count; i++)
            {
                EndChoke(active[gone[i]]);
                active.Remove(gone[i]);
            }
        }

        void CheckNeckSnap()
        {
            bool down = HVRController.GetButtonState(HVRHandSide.Right, HVRButtons.Secondary).Active
                        || HVRController.GetButtonState(HVRHandSide.Left, HVRButtons.Secondary).Active;
            bool pressed = down && !secondaryWasDown;
            secondaryWasDown = down;
            if (!pressed)
                return;

            for (int i = 0; i < hands.Count; i++)
            {
                var h = hands[i];
                if (h == null || !h.IsGrabbing || h.HeldObject == null)
                    continue;

                var obj = h.HeldObject;
                var bridge = obj.GetComponentInParent<ColliderBridge>();
                if (bridge == null || !IsNeck(bridge.damageRegion))
                    continue;

                var breaker = FindNeckBreaker(obj);
                if (breaker != null && !breaker.isBroken)
                {
                    breaker.BreakJoint();
                    MelonLogger.Msg($"{Tag}: neck joint broken on '{breaker.stickHealth?.name}'");
                    continue;
                }

                // Нет брейкера под рукой - бьем тем же типом урона, что и перелом.
                var health = bridge.stickHealth;
                if (health == null)
                    health = obj.GetComponentInParent<StickHealth>();
                if (health != null && !health.isDead)
                {
                    health.ReceivedGeneralDamage(200f, StickHealth.DamageType.BoneBreak, NeckRegion, bridge);
                    MelonLogger.Msg($"{Tag}: neck break damage on '{health.name}' (no joint found)");
                }
            }
        }

        // Части тела НПС, которым можно дать дрожь. Кинематические пропускаем.
        static Rigidbody[] FindTwitchBodies(StickHealth health)
        {
            var parts = health.bodyParts;
            var list = new List<Rigidbody>();
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    var go = parts[i];
                    if (go == null)
                        continue;
                    var rb = go.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic)
                        list.Add(rb);
                }
            }
            return list.ToArray();
        }

        // Колени ищем по зонам урона (kneel/kneer), а не по именам объектов: зоны уже
        // подтверждены логом. Ось сгиба - "вправо" персонажа в осях сустава, поэтому
        // колено сгибается вперед-назад, а не в сторону.
        static void FindKnees(Choke c)
        {
            var h = c.health;
            var journals = new List<ActRagJoint>();
            var joints = new List<ConfigurableJoint>();
            var bases = new List<Quaternion>();
            var axes = new List<Vector3>();

            var ajs = h.actRagJoints;
            var right = h.transform.right;
            if (ajs != null)
            {
                for (int i = 0; i < ajs.Length; i++)
                {
                    var aj = ajs[i];
                    if (aj == null)
                        continue;
                    var region = RegionOf(aj);
                    if (region == null || region.IndexOf("knee", System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var joint = aj.target;
                    if (joint == null)
                        continue;
                    journals.Add(aj);
                    joints.Add(joint);
                    bases.Add(joint.targetRotation);
                    axes.Add(joint.transform.InverseTransformDirection(right).normalized);
                }
            }

            if (joints.Count == 0)
            {
                // Запасной путь, если у ActRagJoint нет ColliderBridge: в таблице
                // суставов 8 = правое колено, 11 = левое (проверено логом).
                var all = h.configurableJoints;
                int[] kneeIdx = { 8, 11 };
                if (all != null)
                {
                    for (int i = 0; i < kneeIdx.Length; i++)
                    {
                        int idx = kneeIdx[i];
                        if (idx >= all.Length)
                            continue;
                        var joint = all[idx];
                        if (joint == null)
                            continue;
                        journals.Add(null);
                        joints.Add(joint);
                        bases.Add(joint.targetRotation);
                        axes.Add(joint.transform.InverseTransformDirection(right).normalized);
                    }
                }
            }

            c.knees = joints.ToArray();
            c.kneeBase = bases.ToArray();
            c.kneeAxis = axes.ToArray();
            c.kneeScripts = journals.ToArray();
            c.kneeScriptWas = new bool[c.kneeScripts.Length];
        }

        static string RegionOf(ActRagJoint aj)
        {
            var bridge = aj.GetComponent<ColliderBridge>();
            if (bridge == null)
                bridge = aj.GetComponentInParent<ColliderBridge>();
            if (bridge == null && aj.target != null)
                bridge = aj.target.GetComponent<ColliderBridge>();
            return bridge != null ? bridge.damageRegion : null;
        }

        static StickJointBreaker FindNeckBreaker(HVRGrabbable obj)
        {
            var t = obj.transform;
            for (int depth = 0; t != null && depth < 12; depth++, t = t.parent)
            {
                var jb = t.GetComponent<StickJointBreaker>();
                if (jb != null && IsNeck(jb.dmgRegion))
                    return jb;
            }
            return null;
        }

        void EnsureHands()
        {
            if (hands.Count > 0)
                return;
            if (Time.unscaledTime < nextScan)
                return;
            nextScan = Time.unscaledTime + 2f;

            var found = UnityEngine.Object.FindObjectsOfType<HVRHandGrabber>();
            hands.Clear();
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                    hands.Add(found[i]);
            }
            if (hands.Count > 0)
                MelonLogger.Msg($"{Tag}: {hands.Count} hand grabber(s)");
        }

        static bool IsNeck(string region)
            => region != null && region.IndexOf(NeckRegion, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
