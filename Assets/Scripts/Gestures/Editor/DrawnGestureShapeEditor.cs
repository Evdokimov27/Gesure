#if UNITY_EDITOR
using System.Collections.Generic;
using GestureRecognition;
using UnityEditor;
using UnityEngine;

namespace GestureRecognition.Editor
{
    [CustomEditor(typeof(DrawnGestureShape))]
    public class DrawnGestureShapeEditor : UnityEditor.Editor
    {
        private bool isDrawing;
        private Vector2 lastDrawnPoint = new Vector2(float.NaN, float.NaN);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "points");

            EditorGUILayout.Space();
            DrawCanvas(target as DrawnGestureShape);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawCanvas(DrawnGestureShape shape)
        {
            if (shape == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Gesture Shape", EditorStyles.boldLabel);
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(260f), GUILayout.ExpandWidth(true));
            rect = EditorGUI.IndentedRect(rect);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
            }

            DrawGrid(rect);
            DrawStroke(rect, shape);
            HandleInput(rect, shape);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear"))
                {
                    Undo.RecordObject(shape, "Clear Gesture Points");
                    shape.EditablePoints.Clear();
                    EditorUtility.SetDirty(shape);
                    lastDrawnPoint = new Vector2(float.NaN, float.NaN);
                }

                using (new EditorGUI.DisabledScope(shape.EditablePoints.Count == 0))
                {
                    if (GUILayout.Button("Normalize"))
                    {
                        Undo.RecordObject(shape, "Normalize Gesture Points");
                        NormalizePoints(shape.EditablePoints);
                        EditorUtility.SetDirty(shape);
                    }
                }
            }

            EditorGUILayout.HelpBox("Left click and drag within the preview to draw the gesture. Right click removes the last point.", MessageType.Info);
        }

        private void DrawGrid(Rect rect)
        {
            Handles.BeginGUI();
            Color gridColor = new Color(1f, 1f, 1f, 0.05f);
            const int divisions = 10;
            for (int i = 1; i < divisions; i++)
            {
                float x = Mathf.Lerp(rect.xMin, rect.xMax, i / (float)divisions);
                Handles.color = gridColor;
                Handles.DrawLine(new Vector3(x, rect.yMin), new Vector3(x, rect.yMax));

                float y = Mathf.Lerp(rect.yMin, rect.yMax, i / (float)divisions);
                Handles.DrawLine(new Vector3(rect.xMin, y), new Vector3(rect.xMax, y));
            }

            Handles.color = new Color(1f, 1f, 1f, 0.2f);
            Vector2 center = rect.center;
            Handles.DrawLine(new Vector3(rect.xMin, center.y), new Vector3(rect.xMax, center.y));
            Handles.DrawLine(new Vector3(center.x, rect.yMin), new Vector3(center.x, rect.yMax));
            Handles.EndGUI();
        }

        private void DrawStroke(Rect rect, DrawnGestureShape shape)
        {
            IReadOnlyList<Vector2> points = shape.Points;
            if (points == null || points.Count == 0)
            {
                return;
            }

            Handles.BeginGUI();
            Handles.color = Color.cyan;
            for (int i = 1; i < points.Count; i++)
            {
                Handles.DrawLine(CanvasToScreen(points[i - 1], rect), CanvasToScreen(points[i], rect));
            }

            if (shape.ClosedLoop && points.Count > 2)
            {
                Handles.DrawLine(CanvasToScreen(points[points.Count - 1], rect), CanvasToScreen(points[0], rect));
            }

            Handles.color = new Color(0.9f, 0.9f, 0.9f, 0.7f);
            const float radius = 4f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 screen = CanvasToScreen(points[i], rect);
                Handles.DrawSolidDisc(screen, Vector3.forward, radius);
            }

            Handles.EndGUI();
        }

        private void HandleInput(Rect rect, DrawnGestureShape shape)
        {
            Event e = Event.current;
            if (e == null)
            {
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition))
                    {
                        if (e.button == 0)
                        {
                            GUIUtility.hotControl = controlId;
                            isDrawing = true;
                            AddPoint(shape, rect, e.mousePosition, true);
                            e.Use();
                        }
                        else if (e.button == 1)
                        {
                            GUIUtility.hotControl = controlId;
                            RemoveLastPoint(shape);
                            e.Use();
                        }
                    }
                    break;
                case EventType.MouseDrag:
                    if (isDrawing && GUIUtility.hotControl == controlId && e.button == 0)
                    {
                        AddPoint(shape, rect, e.mousePosition, false);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId && e.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        isDrawing = false;
                        lastDrawnPoint = new Vector2(float.NaN, float.NaN);
                        e.Use();
                    }
                    break;
            }
        }

        private void AddPoint(DrawnGestureShape shape, Rect rect, Vector2 mousePosition, bool force)
        {
            Vector2 canvasPoint = ScreenToCanvas(mousePosition, rect);
            canvasPoint.x = Mathf.Clamp(canvasPoint.x, -1f, 1f);
            canvasPoint.y = Mathf.Clamp(canvasPoint.y, -1f, 1f);

            List<Vector2> points = shape.EditablePoints;
            float spacing = Mathf.Max(0.001f, shape.EditorPointSpacing);

            Vector2 last = points.Count > 0 ? points[points.Count - 1] : new Vector2(float.NaN, float.NaN);
            if (!force)
            {
                if (!float.IsNaN(lastDrawnPoint.x) && Vector2.Distance(lastDrawnPoint, canvasPoint) < spacing)
                {
                    return;
                }

                if (!float.IsNaN(last.x) && Vector2.Distance(last, canvasPoint) < spacing * 0.5f)
                {
                    return;
                }
            }

            Undo.RecordObject(shape, "Draw Gesture Point");
            points.Add(canvasPoint);
            lastDrawnPoint = canvasPoint;
            EditorUtility.SetDirty(shape);
            Repaint();
        }

        private void RemoveLastPoint(DrawnGestureShape shape)
        {
            List<Vector2> points = shape.EditablePoints;
            if (points.Count == 0)
            {
                return;
            }

            Undo.RecordObject(shape, "Remove Gesture Point");
            points.RemoveAt(points.Count - 1);
            lastDrawnPoint = new Vector2(float.NaN, float.NaN);
            EditorUtility.SetDirty(shape);
            Repaint();
        }

        private static void NormalizePoints(List<Vector2> points)
        {
            if (points == null || points.Count == 0)
            {
                return;
            }

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
            {
                centroid += points[i];
            }

            centroid /= points.Count;
            float maxMagnitude = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                points[i] -= centroid;
                maxMagnitude = Mathf.Max(maxMagnitude, Mathf.Abs(points[i].x), Mathf.Abs(points[i].y));
            }

            if (maxMagnitude < 1e-5f)
            {
                return;
            }

            float inv = 1f / maxMagnitude;
            for (int i = 0; i < points.Count; i++)
            {
                points[i] *= inv;
                points[i] = new Vector2(Mathf.Clamp(points[i].x, -1f, 1f), Mathf.Clamp(points[i].y, -1f, 1f));
            }
        }

        private static Vector2 CanvasToScreen(Vector2 point, Rect rect)
        {
            float x = rect.center.x + point.x * (rect.width * 0.5f);
            float y = rect.center.y - point.y * (rect.height * 0.5f);
            return new Vector2(x, y);
        }

        private static Vector2 ScreenToCanvas(Vector2 screen, Rect rect)
        {
            float x = (screen.x - rect.center.x) / (rect.width * 0.5f);
            float y = -(screen.y - rect.center.y) / (rect.height * 0.5f);
            return new Vector2(x, y);
        }
    }
}
#endif
