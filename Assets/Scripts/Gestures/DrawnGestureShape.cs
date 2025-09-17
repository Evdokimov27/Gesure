using System;
using System.Collections.Generic;
using UnityEngine;

namespace GestureRecognition
{
    /// <summary>
    /// Gesture shape defined by a hand drawn polyline that is compared against the tracked motion.
    /// The recorded samples are projected to the gesture plane, normalised, and aligned using
    /// a least-squares rotation before measuring the average deviation.
    /// </summary>
    [CreateAssetMenu(menuName = "Gestures/Drawn Shape", fileName = "DrawnGesture")]
    public class DrawnGestureShape : GestureShape
    {
        [SerializeField]
        [Tooltip("Treat the stroke as a closed loop by connecting the last point back to the first.")]
        private bool closedLoop = true;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("Minimum planar distance travelled by the tracked object before a comparison is attempted.")]
        private float minimumPathLength = 0.3f;

        [SerializeField]
        [Range(8, 256)]
        [Tooltip("Number of evenly spaced samples used when comparing the motion to the template.")]
        private int comparisonPointCount = 64;

        [SerializeField]
        [Range(0.005f, 0.3f)]
        [Tooltip("Maximum allowed average error after normalising and aligning the paths.")]
        private float maxAverageError = 0.08f;

        [SerializeField]
        [Tooltip("Allow the gesture to match when the motion is mirrored horizontally.")]
        private bool allowMirrored = true;

        [SerializeField]
        [Tooltip("Allow the gesture to match when the motion is performed in reverse order.")]
        private bool allowReversed = true;

        [SerializeField]
        [Range(0f, 0.5f)]
        [Tooltip("Maximum ratio between the end point gap and travelled distance for closed loops.")]
        private float closureDistanceRatio = 0.15f;

        [SerializeField]
        [Range(0.001f, 0.2f)]
        [Tooltip("Minimum separation between points when drawing in the editor preview (editor only).")]
        private float editorPointSpacing = 0.02f;

        [SerializeField]
        [HideInInspector]
        private List<Vector2> points = new List<Vector2>();

        /// <summary>
        /// Recorded points describing the gesture in a two dimensional plane.
        /// </summary>
        public IReadOnlyList<Vector2> Points => points;

        /// <summary>
        /// Whether the stroke should be treated as a closed loop.
        /// </summary>
        public bool ClosedLoop => closedLoop;

        /// <summary>
        /// Minimum separation enforced between recorded points while drawing.
        /// </summary>
        public float EditorPointSpacing => editorPointSpacing;

#if UNITY_EDITOR
        internal List<Vector2> EditablePoints => points;
#endif

        public override bool TryMatch(GestureDetector detector, GestureDetector.TrackedObject tracked, List<GestureDetector.Sample> samples, out GestureDetector.GestureMatch match)
        {
            match = default;

            if (points == null || points.Count < 2)
            {
                return false;
            }

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

            Vector3 axisX = DetermineProjectionAxis(samples, centroid, normal);
            if (axisX.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            axisX.Normalize();
            Vector3 axisY = Vector3.Cross(normal, axisX).normalized;

            List<Vector2> projected = new List<Vector2>(samples.Count);
            float planarDistance = 0f;
            bool hasPrevious = false;
            Vector2 previous = Vector2.zero;

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 offset = samples[i].position - centroid;
                Vector2 projectedPoint = new Vector2(Vector3.Dot(offset, axisX), Vector3.Dot(offset, axisY));
                projected.Add(projectedPoint);

                if (hasPrevious)
                {
                    planarDistance += Vector2.Distance(previous, projectedPoint);
                }

                previous = projectedPoint;
                hasPrevious = true;
            }

            if (planarDistance < minimumPathLength)
            {
                return false;
            }

            bool treatAsLoop = closedLoop && projected.Count > 2;
            if (treatAsLoop)
            {
                float endGap = Vector2.Distance(projected[0], projected[projected.Count - 1]);
                float allowableGap = Mathf.Max(closureDistanceRatio * planarDistance, minimumPathLength * 0.05f);
                if (endGap > allowableGap)
                {
                    return false;
                }
            }

            Vector2[] sampleResampled = Resample(projected, comparisonPointCount, treatAsLoop);
            if (sampleResampled == null)
            {
                return false;
            }

            Vector2[] templateResampled = Resample(points, comparisonPointCount, closedLoop && points.Count > 2);
            if (templateResampled == null)
            {
                return false;
            }

            Vector2[] sampleNormalised = Normalise(sampleResampled, out _, out float sampleScale);
            if (sampleScale < 1e-6f)
            {
                return false;
            }

            Vector2[] templateNormalised = Normalise(templateResampled, out _, out float templateScale);
            if (templateScale < 1e-6f)
            {
                return false;
            }

            float bestError = CalculateBestAlignmentError(sampleNormalised, templateNormalised);
            if (float.IsNaN(bestError) || bestError > maxAverageError)
            {
                return false;
            }

            float travelDistance = GestureDetector.CalculateTravelDistance(samples);
            Vector3 start = samples[0].position;
            Vector3 end = samples[samples.Count - 1].position;
            Vector3 displacement = end - start;

            match = new GestureDetector.GestureMatch
            {
                shape = this,
                target = tracked.target,
                center = centroid,
                normal = normal,
                startPosition = start,
                endPosition = end,
                duration = samples[samples.Count - 1].time - samples[0].time,
                travelDistance = travelDistance,
                travelDirection = displacement.sqrMagnitude > 1e-6f ? displacement.normalized : Vector3.zero,
                radius = 0f,
                coverageAngle = 0f,
                isClockwise = treatAsLoop ? ComputeSignedArea(sampleResampled) < 0f : false,
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

            if (closedLoop)
            {
                Gizmos.DrawLine(match.sampledPositions[match.sampledPositions.Length - 1], match.sampledPositions[0]);
            }
        }

        private static Vector3 DetermineProjectionAxis(List<GestureDetector.Sample> samples, Vector3 centroid, Vector3 normal)
        {
            Vector3 axis = Vector3.zero;
            if (samples.Count >= 2)
            {
                axis = samples[samples.Count - 1].position - samples[0].position;
            }

            if (axis.sqrMagnitude < 1e-6f && samples.Count >= 2)
            {
                axis = samples[1].position - samples[0].position;
            }

            axis = Vector3.ProjectOnPlane(axis, normal);
            if (axis.sqrMagnitude < 1e-6f)
            {
                axis = Vector3.ProjectOnPlane(Vector3.right, normal);
                if (axis.sqrMagnitude < 1e-6f)
                {
                    axis = Vector3.ProjectOnPlane(Vector3.up, normal);
                }
            }

            return axis;
        }

        private static Vector2[] Resample(IReadOnlyList<Vector2> source, int targetCount, bool closed)
        {
            if (source == null || source.Count < 2 || targetCount < 2)
            {
                return null;
            }

            float totalLength = CalculatePathLength(source, closed);
            if (totalLength < 1e-6f)
            {
                return null;
            }

            Vector2[] result = new Vector2[targetCount];
            if (!closed)
            {
                result[0] = source[0];
                result[targetCount - 1] = source[source.Count - 1];
            }

            float segmentCount = closed ? targetCount : targetCount - 1;
            for (int i = 0; i < targetCount; i++)
            {
                if (!closed && (i == 0 || i == targetCount - 1))
                {
                    continue;
                }

                float t = segmentCount > 0f ? i / segmentCount : 0f;
                float distanceAlong = Mathf.Clamp01(t) * totalLength;
                result[i] = InterpolateAtDistance(source, distanceAlong, closed);
            }

            return result;
        }

        private static float CalculatePathLength(IReadOnlyList<Vector2> points, bool closed)
        {
            float total = 0f;
            int count = points.Count;
            int limit = closed ? count : count - 1;

            for (int i = 0; i < limit; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % count];
                total += Vector2.Distance(a, b);
            }

            return total;
        }

        private static Vector2 InterpolateAtDistance(IReadOnlyList<Vector2> points, float distance, bool closed)
        {
            int count = points.Count;
            int segmentCount = closed ? count : count - 1;
            float accumulated = 0f;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % count];
                float segment = Vector2.Distance(a, b);
                if (segment <= 1e-6f)
                {
                    continue;
                }

                if (distance <= accumulated + segment || i == segmentCount - 1)
                {
                    float t = Mathf.Clamp01((distance - accumulated) / segment);
                    return Vector2.Lerp(a, b, t);
                }

                accumulated += segment;
            }

            return points[closed ? 0 : count - 1];
        }

        private static Vector2[] Normalise(Vector2[] points, out Vector2 centroid, out float scale)
        {
            centroid = Vector2.zero;
            scale = 0f;

            if (points == null || points.Length == 0)
            {
                return Array.Empty<Vector2>();
            }

            Vector2[] result = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                centroid += points[i];
            }

            centroid /= points.Length;

            float sumSquares = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 offset = points[i] - centroid;
                sumSquares += offset.sqrMagnitude;
            }

            scale = Mathf.Sqrt(sumSquares / points.Length);
            float invScale = scale > 1e-6f ? 1f / scale : 0f;

            for (int i = 0; i < points.Length; i++)
            {
                result[i] = (points[i] - centroid) * invScale;
            }

            return result;
        }

        private float CalculateBestAlignmentError(Vector2[] sample, Vector2[] template)
        {
            float bestError = float.PositiveInfinity;

            EvaluateVariant(sample, template, ref bestError);

            if (allowReversed)
            {
                Vector2[] reversed = CreateReversed(template);
                EvaluateVariant(sample, reversed, ref bestError);
                if (allowMirrored)
                {
                    Vector2[] mirroredReversed = CreateMirrored(reversed);
                    EvaluateVariant(sample, mirroredReversed, ref bestError);
                }
            }

            if (allowMirrored)
            {
                Vector2[] mirrored = CreateMirrored(template);
                EvaluateVariant(sample, mirrored, ref bestError);
            }

            return bestError;
        }

        private static void EvaluateVariant(Vector2[] sample, Vector2[] template, ref float bestError)
        {
            if (template.Length != sample.Length || template.Length == 0)
            {
                return;
            }

            float numerator = 0f;
            float denominator = 0f;
            for (int i = 0; i < template.Length; i++)
            {
                Vector2 t = template[i];
                Vector2 s = sample[i];
                numerator += t.x * s.y - t.y * s.x;
                denominator += t.x * s.x + t.y * s.y;
            }

            float angle = Mathf.Atan2(numerator, denominator);
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            float error = 0f;
            for (int i = 0; i < template.Length; i++)
            {
                Vector2 t = template[i];
                Vector2 rotated = new Vector2(t.x * cos - t.y * sin, t.x * sin + t.y * cos);
                Vector2 diff = rotated - sample[i];
                error += diff.sqrMagnitude;
            }

            error = Mathf.Sqrt(error / template.Length);
            if (error < bestError)
            {
                bestError = error;
            }
        }

        private static Vector2[] CreateReversed(Vector2[] points)
        {
            Vector2[] reversed = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                reversed[i] = points[points.Length - 1 - i];
            }

            return reversed;
        }

        private static Vector2[] CreateMirrored(Vector2[] points)
        {
            Vector2[] mirrored = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                mirrored[i] = new Vector2(-points[i].x, points[i].y);
            }

            return mirrored;
        }

        private static float ComputeSignedArea(Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3)
            {
                return 0f;
            }

            float area = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Length];
                area += (a.x * b.y) - (b.x * a.y);
            }

            return area * 0.5f;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            comparisonPointCount = Mathf.Clamp(comparisonPointCount, 8, 256);
            maxAverageError = Mathf.Clamp(maxAverageError, 0.005f, 0.3f);
            minimumPathLength = Mathf.Max(0.01f, minimumPathLength);
            editorPointSpacing = Mathf.Clamp(editorPointSpacing, 0.001f, 0.2f);
            closureDistanceRatio = Mathf.Clamp01(closureDistanceRatio);
        }
#endif
    }
}
