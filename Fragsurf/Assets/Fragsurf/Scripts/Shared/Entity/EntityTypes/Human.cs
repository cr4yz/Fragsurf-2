using Fragsurf.Maps;
using Fragsurf.Movement;
using Fragsurf.Shared.Packets;
using Fragsurf.Shared.Player;
using System.Collections.Generic;
using UnityEngine;

namespace Fragsurf.Shared.Entity
{
    public class Human : NetEntity, IDamageable
    {

        public MovementController MovementController;
        public CameraController CameraController;
        public EntityAnimationController AnimationController;

        private Interactor _interactor;
        private int _ownerId = -1;
        private bool _hasAuthorityNextTick;

        public Human(FSGameLoop game) 
            : base(game)
        {
            _autoRecordTimeline = false;
            _interactor = new Interactor(this);
        }

        public static Human Local { get; set; }
        public HumanGameObject HumanGameObject => EntityGameObject as HumanGameObject;
        public bool IsFirstPerson { get; set; }
        public EquippableManager Equippables { get; private set; } = new EquippableManager();

        [NetProperty(true)]
        public virtual Vector3 Velocity { get; set; }
        [NetProperty(true)]
        public virtual Vector3 BaseVelocity { get; set; }
        [NetProperty]
        public int OwnerId
        {
            get => _ownerId;
            set => SetOwnerId(value);
        }
        [NetProperty]
        public bool Ducked { get; set; }

        public bool Dead => false;

        protected override void _Start()
        {
            if(Game.GamemodeLoader.Gamemode.Data.HumanPrefab != null)
            {
                var clone = GameObject.Instantiate(Game.GamemodeLoader.Gamemode.Data.HumanPrefab);
                if (!clone.TryGetComponent(out HumanGameObject huObj))
                {
                    GameObject.Destroy(clone);
                    Debug.LogError("Human Prefab must contain a complete HumanGameObject component");
                }
                else
                {
                    EntityGameObject = huObj;
                }
            }

            if (!EntityGameObject)
            {
                EntityGameObject = new GameObject("Human").AddComponent<HumanGameObject>();
            }

            MovementController = new CSMovementController(this);
            CameraController = new FirstPersonCameraController(this);
        }

        protected override void _Delete()
        {
            if(Local == this)
            {
                Local = null;
            }
        }

        protected override void _Tick()
        {
            // gives it one tick to update origin & angles from authority before taking control
            if (_hasAuthorityNextTick)
            {
                _hasAuthorityNextTick = false;
                InterpolationMode = InterpolationMode.Frame;
                HasAuthority = true;
            }

            TickPunches();
        }

        protected override void _Update()
        {
            MovementController?.Update();
            AnimationController?.Update();
        }

        public Ray GetEyeRay()
        {
            var direction = (Quaternion.Euler(Angles + TotalViewPunch() + TotalAimPunch()) * Vector3.forward).normalized;
            var eyePosition = Origin + (Ducked ? HumanGameObject.DuckedEyeOffset : HumanGameObject.EyeOffset);
            return new Ray(eyePosition, direction);
        }

        public virtual void RunCommand(UserCmd cmd, bool prediction)
        {
            MovementController?.ProcessInput(cmd);
            MovementController?.RunCommand(cmd.Fields, prediction);

            if(Game.IsHost || prediction)
            {
                Equippables?.RunCommand(cmd.Fields);
                _interactor?.RunCommand(cmd.Fields);

                if(Timeline != null && Timeline.Mode == TimelineMode.Record)
                {
                    Timeline.RecordTick();
                }
            }

            if (EntityGameObject)
            {
                EntityGameObject.SendMessage("OnHumanRunCommand");
            }
        }

        public void Spawn(int teamNumber = 0)
        {
            Map.GetSpawnPoint(out Vector3 pos, out Vector3 angles, teamNumber);
            Origin = pos;
            Angles = angles;
            Velocity = Vector3.zero;
            BaseVelocity = Vector3.zero;
        }

        public void Give(string itemName)
        {
            if (!Game.IsHost)
            {
                return;
            }
            var equippable = Game.EntityManager.SpawnEquippable();
            equippable.ItemName = itemName;
            equippable.HumanId = EntityId;
        }

        private void OnKilled()
        {
            // disable colliders (maybe disable gameobject while ragdoll is active?)
        }

        private void OnSpawned()
        {
            
        }

        private void SetOwnerId(int value)
        {
            _ownerId = value;
            if (value == Game.ClientIndex)
            {
                Local = this;
                _hasAuthorityNextTick = true;
            }
            var player = Game.PlayerManager.FindPlayer(value);
            if(player != null)
            {
                player.Entity = this;
            }
        }

        public int HammerVelocity(bool horizontalOnly = true)
        {
            var vel = Velocity;
            if (horizontalOnly)
            {
                vel.y = 0;
            }
            return (int)(vel.magnitude / .0254f);
        }

        public void ClampVelocity(int xzMax, int yMax)
        {
            var maxY = yMax * .0254f;
            var maxXZ = xzMax * .0254f;
            var vel = Velocity;
            var xz = new Vector3(vel.x, 0, vel.z);
            xz = Vector3.ClampMagnitude(xz, maxXZ);
            xz.y = Mathf.Clamp(vel.y, -maxY, maxY);
            Velocity = xz;
        }

        private struct PunchData
        {
            public float InTime;
            public float StartTime;
            public Vector3 View;
            public Vector3 Aim;
            public Vector3 CurView;
            public Vector3 CurAim;
        }

        private List<PunchData> _punches = new List<PunchData>(128);
        public void Punch(Vector3 view, Vector3 aim)
        {
            _punches.Add(new PunchData()
            {
                StartTime = Game.ElapsedTime,
                View = view,
                Aim = aim,
                CurAim = Vector3.zero,
                CurView = Vector3.zero
            });
        }

        public Vector3 TotalAimPunch()
        {
            var result = Vector3.zero;
            foreach (var p in _punches)
            {
                result += p.CurAim;
            }
            return result;
        }

        public Vector3 TotalViewPunch()
        {
            var result = Vector3.zero;
            foreach (var p in _punches)
            {
                result += p.CurView;
            }
            return result;
        }

        private void TickPunches()
        {
            for (int i = _punches.Count - 1; i >= 0; i--)
            {
                if (_punches[i].InTime < 0.05f)
                {
                    var pd = _punches[i];
                    pd.InTime += Time.fixedDeltaTime;
                    pd.CurView = Vector3.Lerp(Vector3.zero, pd.View, pd.InTime / 0.05f);
                    pd.CurAim = Vector3.Lerp(Vector3.zero, pd.Aim, pd.InTime / 0.05f);
                    _punches[i] = pd;
                }
                else
                {
                    var t = Game.ElapsedTime - _punches[i].StartTime;
                    var view = _punches[i].View.SmoothStep(Vector3.zero, t);
                    var aim = _punches[i].Aim.SmoothStep(Vector3.zero, t);
                    if (view == Vector3.zero && aim == Vector3.zero)
                    {
                        _punches.RemoveAt(i);
                    }
                    else
                    {
                        var rep = _punches[i];
                        rep.CurAim = aim;
                        rep.CurView = view;
                        _punches[i] = rep;
                    }
                }
            }
        }

        public void Damage(DamageInfo dmgInfo)
        {
            if (!Game.IsHost)
            {
                if (GameData.Instance.TryGetImpactPrefab(ImpactType.Bullet, SurfaceConfigurator.SurfaceType.Flesh, out GameObject prefab))
                {
                    var effect = Game.Pool.Get(prefab, 10f);
                    effect.transform.position = dmgInfo.HitPoint;
                    effect.transform.forward = dmgInfo.HitNormal;
                }
            }
        }

    }
}

