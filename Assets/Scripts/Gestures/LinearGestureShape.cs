using System.Collections.Generic;
using UnityEngine;

namespace GestureRecognition
{
    /// <summary>
    /// Detects predominantly linear motions with an optional direction constraint.
    /// Useful for gestures such as diagonal swipes.
    /// </summary>
    [CreateAssetMenu(menuName = "Gestures/Linear Shape", fileName = "LinearGesture")]
    public class LinearGestureShape : GestureShape
    {
        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Minimum displacement between the first and last sample (world units).")]
        private float minimumDistance = 0.5f;

        [SerializeField]
        [Tooltip("Maximum allowed deviation from the best fit line (world units).")]
        private float maxDeviationFromLine = 0.15f;

        [SerializeField]
        [Range(0.5f, 1f)]
        [Tooltip("Minimum ratio between straight line distance and travelled path length.")]
        private float minimumStraightness = 0.8f;

        [SerializeField]
        [Tooltip("If enabled the resulting direction must align with the expected vector within the tolerance.")]
        private bool enforceDirection = true;

        [SerializeField]
        [Tooltip("Direction the gesture should follow when direction enforcement is enabled.")]
        private Vector3 expectedDirection = new Vector3(1f, 1f, 0f);

        [SerializeField]
        [Range(0f, 90f)]
        [Tooltip("Maximum allowed angular deviation (degrees) when validating the direction.")]
        private float directionTolerance = 20f;

        [SerializeField]
        [Tooltip("Allow the gesture to be performed in the reverse direction as well.")]
        private bool allowReverse = false;

        public override bool TryMatch(GestureDetector detector, GestureDetector.TrackedObject tracked, List<GestureDetector.Sample> samples, out GestureDetector.GestureMatch match)
        {
            match = default;

            if (samples.Count < MinimumSampleCount)
            {
                return false;
            }

            Vector3 start = samples[0].position;
            Vector3 end = samples[samples.Count - 1].position;
            Vector3 displacement = end - start;
            float straightDistance = displacement.magnitude;
            if (straightDistance < minimumDistance)
            {
                return false;
            }

            Vector3 direction = displacement / straightDistance;

            if (enforceDirection && expectedDirection.sqrMagnitude > 1e-6f)
            {
                Vector3 desired = expectedDirection.normalized;
                float angle = Vector3.Angle(direction, desired);
                float reverseAngle = Vector3.Angle(direction, -desired);
                float bestAngle = allowReverse ? Mathf.Min(angle, reverseAngle) : angle;
                if (bestAngle > directionTolerance)
                {
                    return false;
                }

                if (!allowReverse && Vector3.Dot(direction, desired) < 0f)
                {
                    return false;
                }
            }

            float travelledDistance = 0f;
            float maxDeviation = 0f;
            for (int i = 1; i < samples.Count; i++)
            {
                travelledDistance += Vector3.Distance(samples[i - 1].position, samples[i].position);
            }

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 toPoint = samples[i].position - start;
                Vector3 projected = Vector3.Project(toPoint, direction);
                Vector3 closest = start + projected;
                float deviation = Vector3.Distance(samples[i].position, closest);
                if (deviation > maxDeviation)
                {
                    maxDeviation = deviation;
                }
            }

            if (maxDeviation > maxDeviationFromLine)
            {
                return false;
            }

            float straightness = straightDistance / Mathf.Max(travelledDistance, 1e-5f);
            if (straightness < minimumStraightness)
            {
                return false;
            }

            match = new GestureDetector.GestureMatch
            {
                shape = this,
                center = (start + end) * 0.5f,
                normal = Vector3.zero,
                coverageAngle = 0f,
                radius = 0f,
                travelDistance = travelledDistance,
                travelDirection = direction,
                startPosition = start,
                endPosition = end,
                duration = samples[samples.Count - 1].time - samples[0].time,
                isClockwise = false,
                sampledPositions = ExtractPositions(samples)
            };

            return true;
        }

        public override void DrawGizmos(GestureDetector.GestureMatch match)
        {
            if (match.sampledPositions == null || match.sampledPositions.Length < 2)
            {
                return;
            }

            for (int i = 1; i < match.sampledPositions.Length; i++)
            {
                Gizmos.DrawLine(match.sampledPositions[i - 1], match.sampledPositions[i]);
            }

            Vector3 start = match.startPosition;
            Vector3 end = match.endPosition;
            Gizmos.DrawLine(start, end);

            Vector3 displacement = end - start;
            if (displacement.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector3 direction = displacement.normalized;
            float headLength = Mathf.Min(0.25f, displacement.magnitude * 0.25f);
            if (headLength <= 0f)
            {
                headLength = 0.1f;
            }

            Vector3 headBase = end - direction * headLength;
            Vector3 referenceUp = Vector3.up;
            if (Vector3.Cross(direction, referenceUp).sqrMagnitude < 1e-4f)
            {
                referenceUp = Vector3.right;
            }

            Vector3 side = Vector3.Cross(direction, referenceUp).normalized * headLength * 0.5f;
            Gizmos.DrawLine(end, headBase + side);
            Gizmos.DrawLine(end, headBase - side);
        }

        private void OnValidate()
        {
            minimumDistance = Mathf.Max(0.01f, minimumDistance);
            maxDeviationFromLine = Mathf.Max(0.001f, maxDeviationFromLine);
            minimumStraightness = Mathf.Clamp(minimumStraightness, 0.5f, 1f);
            if (expectedDirection.sqrMagnitude < 1e-6f)
            {
                expectedDirection = new Vector3(1f, 1f, 0f);
            }
        }
    }
}
