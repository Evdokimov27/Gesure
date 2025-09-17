using System.Collections.Generic;
using UnityEngine;

namespace GestureRecognition
{
    /// <summary>
    /// Detects circular or arc shaped motions by fitting the captured samples to a plane and validating radius and coverage.
    /// </summary>
    [CreateAssetMenu(menuName = "Gestures/Circular Shape", fileName = "CircularGesture")]
    public class CircularGestureShape : GestureShape
    {
        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Minimum acceptable radius for the detected gesture (world units).")]
        private float minRadius = 0.1f;

        [SerializeField]
        [Range(0.01f, 0.5f)]
        [Tooltip("Maximum allowed relative standard deviation of the radius across sampled points.")]
        private float radiusVarianceTolerance = 0.2f;

        [SerializeField]
        [Range(0f, 360f)]
        [Tooltip("Minimum angular coverage in degrees before the gesture is considered valid.")]
        private float minCoverageAngle = 300f;

        [SerializeField]
        [Range(0f, 360f)]
        [Tooltip("Maximum angular coverage allowed for the gesture (useful for arcs).")]
        private float maxCoverageAngle = 360f;

        [SerializeField]
        [Range(0.2f, 1.5f)]
        [Tooltip("Required ratio between travelled distance and the ideal arc length.")]
        private float minTravelledArcRatio = 0.75f;

        [SerializeField]
        [Tooltip("If enabled, the detected circle plane must align with the specified normal within the tolerance.")]
        private bool enforceNormalAlignment = false;

        [SerializeField]
        [Tooltip("Desired normal direction when alignment is enforced.")]
        private Vector3 requiredNormal = Vector3.up;

        [SerializeField]
        [Range(0f, 90f)]
        [Tooltip("Maximum angular deviation in degrees when validating the plane normal.")]
        private float normalTolerance = 20f;

        public override bool TryMatch(GestureDetector detector, GestureDetector.TrackedObject tracked, List<GestureDetector.Sample> samples, out GestureDetector.GestureMatch match)
        {
            match = default;

            if (samples.Count < MinimumSampleCount)
            {
                return false;
            }

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < samples.Count; i++)
            {
                centroid += samples[i].position;
            }

            centroid /= samples.Count;
            Vector3 normal = GestureDetector.EstimateNormal(samples, centroid);
            if (normal.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            normal.Normalize();

            if (enforceNormalAlignment)
            {
                Vector3 desired = requiredNormal.sqrMagnitude > 1e-6f ? requiredNormal.normalized : Vector3.up;
                float angle = Vector3.Angle(normal, desired);
                float flippedAngle = Vector3.Angle(normal, -desired);
                if (Mathf.Min(angle, flippedAngle) > normalTolerance)
                {
                    return false;
                }

                if (flippedAngle < angle)
                {
                    normal = -normal;
                }
            }

            Vector3 axisX = Vector3.ProjectOnPlane(samples[0].position - centroid, normal);
            if (axisX.sqrMagnitude < 1e-6f)
            {
                axisX = Vector3.ProjectOnPlane(Vector3.right, normal);
                if (axisX.sqrMagnitude < 1e-6f)
                {
                    axisX = Vector3.ProjectOnPlane(Vector3.up, normal);
                }
            }

            axisX.Normalize();
            Vector3 axisY = Vector3.Cross(normal, axisX).normalized;

            List<float> angles = new List<float>(samples.Count);
            float accumulatedRadius = 0f;
            float travelledDistance = 0f;
            Vector3 previous = samples[0].position;

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 offset = samples[i].position - centroid;
                Vector2 projected = new Vector2(Vector3.Dot(offset, axisX), Vector3.Dot(offset, axisY));
                accumulatedRadius += projected.magnitude;
                angles.Add(Mathf.Atan2(projected.y, projected.x));

                if (i > 0)
                {
                    travelledDistance += Vector3.Distance(samples[i].position, previous);
                }

                previous = samples[i].position;
            }

            float meanRadius = accumulatedRadius / samples.Count;
            if (meanRadius < minRadius)
            {
                return false;
            }

            float totalSquaredError = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 offset = samples[i].position - centroid;
                Vector2 projected = new Vector2(Vector3.Dot(offset, axisX), Vector3.Dot(offset, axisY));
                float difference = projected.magnitude - meanRadius;
                totalSquaredError += difference * difference;
            }

            float standardDeviation = Mathf.Sqrt(totalSquaredError / samples.Count);
            float normalisedDeviation = meanRadius > 1e-5f ? standardDeviation / meanRadius : float.MaxValue;
            if (normalisedDeviation > radiusVarianceTolerance)
            {
                return false;
            }

            float coverage = GestureDetector.CalculateAngularCoverage(angles);
            if (coverage < minCoverageAngle || coverage > maxCoverageAngle)
            {
                return false;
            }

            float circumference = 2f * Mathf.PI * meanRadius;
            float idealArc = circumference * Mathf.Clamp01(coverage / 360f);
            float travelRatio = idealArc > 1e-5f ? travelledDistance / idealArc : 0f;
            if (travelRatio < minTravelledArcRatio)
            {
                return false;
            }

            float totalAngleDelta = 0f;
            if (angles.Count > 1)
            {
                float previousAngle = Mathf.Rad2Deg * angles[0];
                for (int i = 1; i < angles.Count; i++)
                {
                    float currentAngle = Mathf.Rad2Deg * angles[i];
                    totalAngleDelta += Mathf.DeltaAngle(previousAngle, currentAngle);
                    previousAngle = currentAngle;
                }
            }

            Vector3 start = samples[0].position;
            Vector3 end = samples[samples.Count - 1].position;
            Vector3 travelVector = end - start;

            match = new GestureDetector.GestureMatch
            {
                shape = this,
                center = centroid,
                radius = meanRadius,
                normal = normal,
                coverageAngle = coverage,
                travelDistance = travelledDistance,
                travelDirection = travelVector.sqrMagnitude > 1e-6f ? travelVector.normalized : Vector3.zero,
                startPosition = start,
                endPosition = end,
                duration = samples[samples.Count - 1].time - samples[0].time,
                isClockwise = totalAngleDelta < 0f,
                sampledPositions = ExtractPositions(samples)
            };

            return true;
        }

        public override void DrawGizmos(GestureDetector.GestureMatch match)
        {
            if (match.radius <= 0f)
            {
                return;
            }

            Vector3 normal = match.normal.sqrMagnitude > 1e-6f ? match.normal.normalized : Vector3.up;
            Vector3 axisX = Vector3.ProjectOnPlane(Vector3.right, normal);
            if (axisX.sqrMagnitude < 1e-4f)
            {
                axisX = Vector3.ProjectOnPlane(Vector3.up, normal);
            }

            axisX.Normalize();
            Vector3 axisY = Vector3.Cross(normal, axisX).normalized;

            Vector3 previousPoint = match.center + axisX * match.radius;
            const int segments = 64;
            for (int i = 1; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 nextPoint = match.center + (Mathf.Cos(angle) * axisX + Mathf.Sin(angle) * axisY) * match.radius;
                Gizmos.DrawLine(previousPoint, nextPoint);
                previousPoint = nextPoint;
            }
        }

        private void OnValidate()
        {
            minRadius = Mathf.Max(0.01f, minRadius);
            radiusVarianceTolerance = Mathf.Clamp(radiusVarianceTolerance, 0.01f, 0.5f);
            minCoverageAngle = Mathf.Clamp(minCoverageAngle, 0f, 360f);
            maxCoverageAngle = Mathf.Clamp(maxCoverageAngle, minCoverageAngle, 360f);
            minTravelledArcRatio = Mathf.Clamp(minTravelledArcRatio, 0.2f, 1.5f);
            if (requiredNormal.sqrMagnitude < 1e-6f)
            {
                requiredNormal = Vector3.up;
            }
        }
    }
}
