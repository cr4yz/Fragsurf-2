using Fragsurf.Actors;
using Fragsurf.Movement;
using Fragsurf.Shared.Entity;
using Fragsurf.Shared.Player;
using MessagePack;
using System.Collections.Generic;
using UnityEngine;

namespace Fragsurf.Gamemodes.Bunnyhop
{
    [MessagePackObject]
    public class BunnyhopTimeline : GenericEntityTimeline<BunnyhopTimelineFrame>
    {

        [IgnoreMember]
        public readonly FSMTrack Track;
        [IgnoreMember]
        public int Checkpoint = 1;
        [IgnoreMember]
        public int Stage = 1;
        [IgnoreMember]
        public bool RunIsLive = true;

        [IgnoreMember]
        private float _previousYaw;

        public BunnyhopTimeline() { }

        public BunnyhopTimeline(FSMTrack track)
        {
            Track = track;
        }

        protected override BunnyhopTimelineFrame GetFrame(NetEntity ent)
        {
            var frame = LastFrame;
            var hu = ent as Human;
            var vel = hu.Velocity;
            vel.y = 0;
            frame.Tick++;
            frame.Position = ent.Origin;
            frame.Angles = ent.Angles;
            frame.Time += Time.fixedDeltaTime;
            frame.Velocity = (int)(vel.magnitude / SurfController.HammerScale);

            CalculateSync(hu, ref frame);
            CheckJumpsAndStrafes(hu, ref frame);

            return frame;
        }

        private void CheckJumpsAndStrafes(Human human, ref BunnyhopTimelineFrame frame)
        {
            if (human.MovementController is DefaultMovementController move)
            {
                if (move.MoveData.JustJumped)
                {
                    frame.Jumps++;
                }

                var nb = move.MoveData.Buttons;
                var ob = move.MoveData.OldButtons;

                if ((nb.HasFlag(InputActions.MoveLeft) && !ob.HasFlag(InputActions.MoveLeft))
                    || (nb.HasFlag(InputActions.MoveRight) && !ob.HasFlag(InputActions.MoveRight)))
                {
                    frame.Strafes++;
                }
            }
        }

        private void CalculateSync(Human human, ref BunnyhopTimelineFrame frame)
        {
            if (!(human.MovementController is DefaultMovementController move))
            {
                return;
            }

            var nb = move.MoveData.Buttons;

            if (move.GroundObject == null)
            {
                var angleDiff = move.MoveData.ViewAngles.y - _previousYaw;
                if (angleDiff > 180)
                    angleDiff -= 360;
                else if (angleDiff < -180)
                    angleDiff += 360;

                // Add to good sync if client buttons match up
                if (angleDiff < 0)
                {
                    frame.TotalSync++;
                    if (nb.HasFlag(InputActions.MoveLeft) && !nb.HasFlag(InputActions.MoveRight))
                    {
                        frame.GoodSync++;
                    }
                    if (move.MoveData.Velocity.z < 0)
                    {
                        frame.GoodSyncVel++;
                    }
                }
                else if (angleDiff > 0)
                {
                    frame.TotalSync++;
                    if (nb.HasFlag(InputActions.MoveRight) && !nb.HasFlag(InputActions.MoveLeft))
                    {
                        frame.GoodSync++;
                    }
                    if (move.MoveData.Velocity.z > 0)
                    {
                        frame.GoodSyncVel++;
                    }
                }
            }

            frame.FinalSync = frame.TotalSync != 0
                ? (int)(((float)frame.GoodSync / frame.TotalSync) * 100f)
                : (byte)100;

            _previousYaw = human.Angles.y;
        }

        protected override void ApplyFrame(NetEntity ent, BunnyhopTimelineFrame frame)
        {
            ent.Origin = frame.Position;
            ent.Angles = frame.Angles;
        }

    }
}
