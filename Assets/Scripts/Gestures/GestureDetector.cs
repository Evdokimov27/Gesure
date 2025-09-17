using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace GestureRecognition
{
    /// <summary>
    /// Tracks the movement history of configured objects and raises events when a gesture is recognised.
    /// Currently supports detecting circular motion.
    /// </summary>
    public class GestureDetector : MonoBehaviour
    {
        [Serializable]
        public class TrackedObject
        {
            [Tooltip("Transform that will be analysed for gesture recognition.")]
            public Transform target;

            [NonSerialized]
            internal readonly List<Sample> samples = new List<Sample>();

            [NonSerialized]
            internal float lastCircleTime = float.NegativeInfinity;

            [NonSerialized]
            internal bool hasLastCircle;

            [NonSerialized]
            internal Vector3 lastCircleCenter;

            [NonSerialized]
            internal float lastCircleRadius;
        }

        private struct Sample
        {
            public Vector3 position;
            public float time;
        }

        [Serializable]
        public class CircleGestureEvent : UnityEvent<Transform, Vector3, float>
        {
        }

        [Header("Tracked Objects")]
        [SerializeField]
        private List<TrackedObject> trackedObjects = new List<TrackedObject>();

        [SerializeField]
        [Min(0.005f)]
        [Tooltip("Interval in seconds between position samples for each tracked object.")]
        private float sampleInterval = 0.05f;

        [SerializeField]
        [Tooltip("Minimum distance an object must move before a new sample is stored.")]
        private float minPointDistance = 0.005f;

        [SerializeField]
        [Tooltip("Maximum lifetime of stored samples in seconds.")]
        private float maxSampleAge = 3f;

        [SerializeField]
        [Tooltip("Minimum number of samples required before gesture analysis starts.")]
        private int minSampleCount = 25;

        [Header("Circle Detection")]
        [SerializeField]
        [Tooltip("Minimum acceptable radius for a detected circular gesture (world units).")]
        private float minCircleRadius = 0.1f;

        [SerializeField]
        [Range(0.01f, 0.5f)]
        [Tooltip("Maximum allowed relative standard deviation of the radius across sampled points.")]
        private float radiusVarianceTolerance = 0.2f;

        [SerializeField]
        [Range(90f, 360f)]
        [Tooltip("Minimum angular coverage in degrees before a circular gesture is considered complete.")]
        private float minCoverageAngle = 300f;

        [SerializeField]
        [Range(0.2f, 1.5f)]
        [Tooltip("Required ratio between the travelled distance and the circle circumference.")]
        private float minTravelledCircumferenceRatio = 0.75f;

        [SerializeField]
        [Tooltip("Cooldown between detections for the same object to avoid repeated triggers (seconds).")]
        private float detectionCooldown = 1f;

        [Header("Debug")] 
        [SerializeField]
        [Tooltip("If enabled, successful detections are logged to the Unity console.")]
        private bool logDetections = true;

        [SerializeField]
        [Tooltip("Draws the captured trail and last detected circle in the scene view during play mode.")]
        private bool drawDebug = true;

        [SerializeField]
        private Color trailColor = Color.cyan;

        [SerializeField]
        private Color circleColor = Color.green;

        [SerializeField]
        private CircleGestureEvent onCircleDetected = new CircleGestureEvent();

        private float sampleTimer;

        /// <summary>
        /// Event invoked whenever a tracked object performs a circular gesture.
        /// Provides the transform, detected circle center and radius.
        /// </summary>
        public CircleGestureEvent OnCircleDetected => onCircleDetected;

        private void Update()
        {
            sampleTimer += Time.deltaTime;
            if (sampleTimer < sampleInterval)
            {
                return;
            }

            sampleTimer = 0f;
            float currentTime = Time.time;

            for (int i = trackedObjects.Count - 1; i >= 0; i--)
            {
                TrackedObject tracked = trackedObjects[i];
                if (tracked?.target == null)
                {
                    continue;
                }

                SampleObject(tracked, currentTime);

                if (tracked.samples.Count < minSampleCount)
                {
                    continue;
                }

                if (TryDetectCircle(tracked, currentTime, out Vector3 center, out float radius))
                {
                    tracked.lastCircleTime = currentTime;
                    tracked.hasLastCircle = true;
                    tracked.lastCircleCenter = center;
                    tracked.lastCircleRadius = radius;

                    onCircleDetected.Invoke(tracked.target, center, radius);

                    if (logDetections)
                    {
                        Debug.Log($"[{nameof(GestureDetector)}] Circle detected for '{tracked.target.name}' at {center} with radius {radius:0.###}.");
                    }
                }
            }
        }

        /// <summary>
        /// Registers a new transform for gesture detection at runtime.
        /// </summary>
        public void Register(Transform target)
        {
            if (target == null || trackedObjects.Exists(t => t.target == target))
            {
                return;
            }

            trackedObjects.Add(new TrackedObject { target = target });
        }

        /// <summary>
        /// Removes a transform from gesture detection and clears its history.
        /// </summary>
        public void Unregister(Transform target)
        {
            trackedObjects.RemoveAll(t => t.target == target);
        }

        /// <summary>
        /// Clears the stored trail for a specific transform, forcing gesture detection to restart.
        /// </summary>
        public void ClearHistory(Transform target)
        {
            TrackedObject tracked = trackedObjects.Find(t => t.target == target);
            if (tracked != null)
            {
                tracked.samples.Clear();
                tracked.hasLastCircle = false;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawDebug || !Application.isPlaying)
            {
                return;
            }

            foreach (TrackedObject tracked in trackedObjects)
            {
                if (tracked == null || tracked.samples.Count < 2)
                {
                    continue;
                }

                Gizmos.color = trailColor;
                for (int i = 1; i < tracked.samples.Count; i++)
                {
                    Gizmos.DrawLine(tracked.samples[i - 1].position, tracked.samples[i].position);
                }

                if (tracked.hasLastCircle)
                {
                    DrawCircleGizmo(tracked.lastCircleCenter, tracked.lastCircleRadius, tracked.samples);
                }
            }
        }

        private void DrawCircleGizmo(Vector3 center, float radius, List<Sample> samples)
        {
            Gizmos.color = circleColor;

            const int segments = 64;
            Vector3 normal = EstimateNormal(samples, center);
            Vector3 axisX = Vector3.ProjectOnPlane(Vector3.right, normal);
            if (axisX.sqrMagnitude < 1e-4f)
            {
                axisX = Vector3.ProjectOnPlane(Vector3.up, normal);
            }

            axisX.Normalize();
            Vector3 axisY = Vector3.Cross(normal, axisX).normalized;

            Vector3 previousPoint = center + axisX * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 nextPoint = center + (Mathf.Cos(angle) * axisX + Mathf.Sin(angle) * axisY) * radius;
                Gizmos.DrawLine(previousPoint, nextPoint);
                previousPoint = nextPoint;
            }
        }
#endif

        private void SampleObject(TrackedObject tracked, float currentTime)
        {
            Vector3 currentPosition = tracked.target.position;
            List<Sample> samples = tracked.samples;

            if (samples.Count == 0 || (currentPosition - samples[samples.Count - 1].position).sqrMagnitude >= minPointDistance * minPointDistance)
            {
                samples.Add(new Sample { position = currentPosition, time = currentTime });
            }

            // Remove stale samples.
            for (int i = 0; i < samples.Count; i++)
            {
                if (currentTime - samples[i].time <= maxSampleAge)
                {
                    if (i > 0)
                    {
                        samples.RemoveRange(0, i);
                    }
                    return;
                }
            }

            samples.Clear();
            tracked.hasLastCircle = false;
        }

        private bool TryDetectCircle(TrackedObject tracked, float currentTime, out Vector3 center, out float radius)
        {
            center = Vector3.zero;
            radius = 0f;

            if (currentTime - tracked.lastCircleTime < detectionCooldown)
            {
                return false;
            }

            List<Sample> samples = tracked.samples;
            if (samples.Count < minSampleCount)
            {
                return false;
            }

            Vector3 centroid = Vector3.zero;
            foreach (Sample sample in samples)
            {
                centroid += sample.position;
            }

            centroid /= samples.Count;
            Vector3 normal = EstimateNormal(samples, centroid);
            if (normal.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            normal.Normalize();
            Vector3 axisX = Vector3.ProjectOnPlane(samples[samples.Count - 1].position - centroid, normal);
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

            float accumulatedRadius = 0f;
            float totalSquaredError = 0f;
            float travelledDistance = 0f;
            Vector3 previous = samples[0].position;
            List<float> angles = new List<float>(samples.Count);

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 offset = samples[i].position - centroid;
                Vector2 projected = new Vector2(Vector3.Dot(offset, axisX), Vector3.Dot(offset, axisY));

                float pointRadius = projected.magnitude;
                accumulatedRadius += pointRadius;
                angles.Add(Mathf.Atan2(projected.y, projected.x));

                if (i > 0)
                {
                    travelledDistance += Vector3.Distance(samples[i].position, previous);
                }

                previous = samples[i].position;
            }

            radius = accumulatedRadius / samples.Count;
            if (radius < minCircleRadius)
            {
                return false;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 offset = samples[i].position - centroid;
                Vector2 projected = new Vector2(Vector3.Dot(offset, axisX), Vector3.Dot(offset, axisY));
                float difference = projected.magnitude - radius;
                totalSquaredError += difference * difference;
            }

            float standardDeviation = Mathf.Sqrt(totalSquaredError / samples.Count);
            if (standardDeviation / radius > radiusVarianceTolerance)
            {
                return false;
            }

            float coverage = CalculateAngularCoverage(angles);
            if (coverage < minCoverageAngle)
            {
                return false;
            }

            float circumference = 2f * Mathf.PI * radius;
            float travelRatio = travelledDistance / Mathf.Max(circumference, 1e-5f);
            if (travelRatio < minTravelledCircumferenceRatio)
            {
                return false;
            }

            center = centroid;
            return true;
        }

        private static Vector3 EstimateNormal(List<Sample> samples, Vector3 centroid)
        {
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 current = samples[i].position - centroid;
                Vector3 next = samples[(i + 1) % samples.Count].position - centroid;
                normal += Vector3.Cross(current, next);
            }

            return normal;
        }

        private static float CalculateAngularCoverage(List<float> angles)
        {
            angles.Sort();
            float maxGap = 0f;

            for (int i = 0; i < angles.Count - 1; i++)
            {
                float gap = angles[i + 1] - angles[i];
                if (gap > maxGap)
                {
                    maxGap = gap;
                }
            }

            if (angles.Count > 1)
            {
                float wrapGap = (angles[0] + Mathf.PI * 2f) - angles[angles.Count - 1];
                if (wrapGap > maxGap)
                {
                    maxGap = wrapGap;
                }
            }

            float coverage = Mathf.Rad2Deg * ((Mathf.PI * 2f) - maxGap);
            return Mathf.Clamp(coverage, 0f, 360f);
        }
    }
}
