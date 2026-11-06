using System;
using DeadCellsMultiplayerMod.Ghost;

namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    internal readonly struct MobSnapshot
    {
        public readonly double ReceivedFrame;
        public readonly double X;
        public readonly double Y;
        public readonly double Dx;
        public readonly double Dy;
        public readonly int Dir;
        public readonly int Life;
        public readonly int MaxLife;
        public readonly string AnimPayload;
        public readonly string StatePayload;
        public readonly bool ForceSnap;
        public readonly bool ForceVerticalSnap;

        public MobSnapshot(
            double receivedFrame,
            double x,
            double y,
            double dx,
            double dy,
            int dir,
            int life,
            int maxLife,
            string animPayload,
            string statePayload,
            bool forceSnap = false,
            bool forceVerticalSnap = false)
        {
            ReceivedFrame = receivedFrame;
            X = x;
            Y = y;
            Dx = dx;
            Dy = dy;
            Dir = dir;
            Life = life;
            MaxLife = maxLife;
            AnimPayload = animPayload ?? string.Empty;
            StatePayload = statePayload ?? string.Empty;
            ForceSnap = forceSnap;
            ForceVerticalSnap = forceVerticalSnap;
        }
    }

    internal sealed class MobSnapshotBuffer
    {
        public const int Capacity = 8;
        private readonly MobSnapshot[] _buffer = new MobSnapshot[Capacity];
        private int _count;
        private int _head; // Index of newest snapshot

        public int Count => _count;

        public void Clear()
        {
            _count = 0;
            _head = 0;
        }

        public void Add(in MobSnapshot snapshot)
        {
            if (_count == 0)
            {
                _buffer[0] = snapshot;
                _head = 0;
                _count = 1;
                return;
            }

            double frame = snapshot.ReceivedFrame;
            double latestFrame = _buffer[_head].ReceivedFrame;
            if (frame <= latestFrame)
            {
                frame = latestFrame + 0.01;
            }

            var adjusted = Math.Abs(frame - snapshot.ReceivedFrame) > 0.0001
                ? new MobSnapshot(
                    frame,
                    snapshot.X,
                    snapshot.Y,
                    snapshot.Dx,
                    snapshot.Dy,
                    snapshot.Dir,
                    snapshot.Life,
                    snapshot.MaxLife,
                    snapshot.AnimPayload,
                    snapshot.StatePayload,
                    snapshot.ForceSnap,
                    snapshot.ForceVerticalSnap)
                : snapshot;

            _head = (_head + 1) % Capacity;
            _buffer[_head] = adjusted;
            if (_count < Capacity)
                _count++;
        }

        public bool TryGetLatest(out MobSnapshot latest)
        {
            if (_count == 0)
            {
                latest = default;
                return false;
            }
            latest = _buffer[_head];
            return true;
        }

        public bool TryGetAt(int indexFromLatest, out MobSnapshot snapshot)
        {
            if (indexFromLatest < 0 || indexFromLatest >= _count)
            {
                snapshot = default;
                return false;
            }

            int actualIndex = (_head - indexFromLatest) % Capacity;
            if (actualIndex < 0)
                actualIndex += Capacity;

            snapshot = _buffer[actualIndex];
            return true;
        }

        public static double VelocityToPixels(double velocity)
        {
            if (!double.IsFinite(velocity))
                return 0.0;
            if (Math.Abs(velocity) < 0.05)
                return 0.0;
            return Math.Abs(velocity) <= 1.5 ? velocity * 24.0 : velocity;
        }

        public bool SampleHermite(
            double renderFrame,
            out double x,
            out double y,
            out int dir,
            out string animPayload,
            out bool forceSnap,
            out bool forceVerticalSnap)
        {
            if (_count == 0)
            {
                x = 0;
                y = 0;
                dir = 0;
                animPayload = string.Empty;
                forceSnap = false;
                forceVerticalSnap = false;
                return false;
            }

            MobSnapshot newest = _buffer[_head];
            if (renderFrame >= newest.ReceivedFrame)
            {
                // Slightly ahead of the latest snapshot. Extrapolate smoothly with capped distance.
                double elapsed = Math.Clamp(renderFrame - newest.ReceivedFrame, 0.0, 4.0);
                double vx = VelocityToPixels(newest.Dx);
                double vy = VelocityToPixels(newest.Dy);
                x = newest.X + vx * elapsed;
                // Suppress downward vertical extrapolation for small residual velocities (grounded mobs)
                bool allowYExtrap = Math.Abs(newest.Dy) > 0.25;
                y = allowYExtrap ? newest.Y + vy * elapsed : newest.Y;
                dir = newest.Dir;
                animPayload = newest.AnimPayload;
                forceSnap = newest.ForceSnap;
                forceVerticalSnap = newest.ForceVerticalSnap;
                return true;
            }

            if (_count == 1)
            {
                x = newest.X;
                y = newest.Y;
                dir = newest.Dir;
                animPayload = newest.AnimPayload;
                forceSnap = newest.ForceSnap;
                forceVerticalSnap = newest.ForceVerticalSnap;
                return true;
            }

            MobSnapshot s1 = newest;
            MobSnapshot s0 = default;
            bool foundBracket = false;

            for (int i = 0; i < _count - 1; i++)
            {
                TryGetAt(i, out s1);
                TryGetAt(i + 1, out s0);

                if (s0.ReceivedFrame <= renderFrame && renderFrame <= s1.ReceivedFrame)
                {
                    foundBracket = true;
                    break;
                }
            }

            if (!foundBracket)
            {
                // Older than oldest snapshot: clamp to oldest.
                TryGetAt(_count - 1, out var oldest);
                x = oldest.X;
                y = oldest.Y;
                dir = oldest.Dir;
                animPayload = oldest.AnimPayload;
                forceSnap = oldest.ForceSnap;
                forceVerticalSnap = oldest.ForceVerticalSnap;
                return true;
            }

            // Snap if flagged or if position discontinuity exceeds threshold (teleport / map warp)
            double dx = Math.Abs(s1.X - s0.X);
            double dy = Math.Abs(s1.Y - s0.Y);
            if (s1.ForceSnap || s1.ForceVerticalSnap || dx > 240.0 || dy > 240.0)
            {
                x = s1.X;
                y = s1.Y;
                dir = s1.Dir;
                animPayload = s1.AnimPayload;
                forceSnap = s1.ForceSnap;
                forceVerticalSnap = s1.ForceVerticalSnap;
                return true;
            }

            double frameDelta = s1.ReceivedFrame - s0.ReceivedFrame;
            if (frameDelta <= 0.0001)
            {
                x = s1.X;
                y = s1.Y;
                dir = s1.Dir;
                animPayload = s1.AnimPayload;
                forceSnap = s1.ForceSnap;
                forceVerticalSnap = s1.ForceVerticalSnap;
                return true;
            }

            double t = (renderFrame - s0.ReceivedFrame) / frameDelta;
            t = Math.Clamp(t, 0.0, 1.0);

            double v0x = VelocityToPixels(s0.Dx);
            double v1x = VelocityToPixels(s1.Dx);
            double v0y = VelocityToPixels(s0.Dy);
            double v1y = VelocityToPixels(s1.Dy);

            x = CombatSyncHelpers.HermiteInterpolate(s0.X, v0x, s1.X, v1x, t, frameDelta);
            y = CombatSyncHelpers.HermiteInterpolate(s0.Y, v0y, s1.Y, v1y, t, frameDelta);
            dir = t >= 0.5 ? s1.Dir : s0.Dir;
            animPayload = !string.IsNullOrEmpty(s1.AnimPayload) ? s1.AnimPayload : (s0.AnimPayload ?? string.Empty);
            forceSnap = false;
            forceVerticalSnap = false;
            return true;
        }
    }
}
