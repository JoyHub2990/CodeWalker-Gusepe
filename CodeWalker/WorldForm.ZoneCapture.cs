using CodeWalker.Project.Panels;
using CodeWalker.World;
using SharpDX;
using System;
using System.Windows.Forms;

namespace CodeWalker
{
    public partial class WorldForm
    {
        public RageQuitZonePanel ZoneCapturePanel { get; set; }
        private string zonePreviousSelection = "Entity";
        private ToolStripMenuItem zoneSelectionMenu;
        private bool zoneDragging, zoneOrbit;
        private MouseButtons zoneConsumedButtons;
        private Vector3 zoneDragOffset, zoneDragStart;
        private bool ZoneInteraction => ZoneCapturePanel != null && !ZoneCapturePanel.IsDisposed && ZoneCapturePanel.Interacting;

        private void InitializeZoneSelection()
        {
            SelectionModeComboBox.Items.Add("Zone points");
            zoneSelectionMenu = new ToolStripMenuItem("Zone points", null, (_, __) => SetSelectionMode("Zone points"));
            ToolbarSelectButton.DropDownItems.Add(zoneSelectionMenu);
            MouseCaptureChanged += (_, __) =>
            {
                if (!Capture && zoneDragging) { zoneDragging = false; ZoneCapturePanel?.EndDrag(); }
                if (!Capture) { zoneOrbit = false; zoneConsumedButtons = MouseButtons.None; }
            };
        }

        public void ActivateZoneSelection()
        {
            if (SelectionMode != MapSelectionMode.ZonePoints) zonePreviousSelection = SelectionModeStr;
            MouseLButtonDown = MouseRButtonDown = false; ControlBrushEnabled = false;
            GrabbedMarker = null;
            if (GrabbedWidget != null) { GrabbedWidget.IsDragging = false; GrabbedWidget = null; }
            Input.KeyboardStop();
            if (ControlMode != WorldControlMode.Free) SetControlMode(WorldControlMode.Free);
            if (SelectionMode != MapSelectionMode.ZonePoints) SetSelectionMode("Zone points");
        }
        public void RestoreZoneSelection()
        {
            zoneDragging = zoneOrbit = false; zoneConsumedButtons = MouseButtons.None; Capture = false;
            if (SelectionMode == MapSelectionMode.ZonePoints) SetSelectionMode(zonePreviousSelection == "Zone points" ? "Entity" : zonePreviousSelection);
            Cursor = Cursors.Default;
        }

        private Ray ZoneClickRay(int x, int y) => ZoneGeometry.ClickRay(camera.ViewProjInvMatrix, camera.Position,
            camera.IsOrthographic || camera.IsMapView, x, y, (int)camera.Width, (int)camera.Height);
        private static bool ZonePlaneHit(Ray ray, float height, out Vector3 point)
        {
            point = Vector3.Zero;
            if (Math.Abs(ray.Direction.Z) < .00001f) return false;
            float t = (height - ray.Position.Z) / ray.Direction.Z;
            if (t <= 0 || t > 100000) return false;
            point = ray.Position + ray.Direction * t; return true;
        }
        private bool ZoneSurfaceHit(Ray ray, out Vector3 point)
        {
            float nearest = float.MaxValue;
            foreach (var item in Renderer.ZonePickGeometries)
                nearest = ZoneGeometry.Raycast(item.Geometry, item.Transform, ray, nearest);
            point = nearest < float.MaxValue ? ray.Position + ray.Direction * nearest : Vector3.Zero;
            return nearest < float.MaxValue;
        }
        private bool ZoneScreenPoint(Vector3 point, out Vector2 screen)
        {
            var clip = Vector4.Transform(new Vector4(point - camera.Position, 1), camera.ViewProjMatrix);
            screen = Vector2.Zero;
            if (clip.W <= 0) return false;
            var ndc = clip.XYZ() / clip.W;
            if (ndc.Z < 0 || ndc.Z > 1) return false;
            screen = new Vector2((ndc.X + 1) * camera.Width * .5f, (1 - ndc.Y) * camera.Height * .5f); return true;
        }
        private int ZonePickPoint(Vector3[] points, int x, int y)
        {
            int best = -1; float distance = 12 * 12;
            for (int i = 0; i < points.Length; i++)
                if (ZoneScreenPoint(points[i], out var p))
                { float d = (p - new Vector2(x, y)).LengthSquared(); if (d < distance) { distance = d; best = i; } }
            return best;
        }
        private bool ZoneInsertOnEdge(MouseEventArgs e, Ray ray)
        {
            var panel = ZoneCapturePanel; var points = panel.Outline;
            float nearest = 12 * 12; int edge = -1; float fraction = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (!ZoneScreenPoint(points[i], out var a) || !ZoneScreenPoint(points[(i + 1) % points.Length], out var b)) continue;
                var d = b - a; if (d.LengthSquared() < .00001f) continue;
                float t = Math.Max(0, Math.Min(1, Vector2.Dot(new Vector2(e.X, e.Y) - a, d) / d.LengthSquared()));
                float distance = (a + t * d - new Vector2(e.X, e.Y)).LengthSquared();
                if (distance < nearest) { nearest = distance; edge = i; fraction = t; }
            }
            if (edge < 0) return false;
            var world = Vector3.Lerp(points[edge], points[(edge + 1) % points.Length], fraction);
            if (ZonePlaneHit(ray, world.Z, out var hit)) world = hit;
            panel.InsertPoint(edge, world); return true;
        }

        private bool ZoneCaptureMouseDown(MouseEventArgs e)
        {
            if (!ZoneInteraction) return false;
            var panel = ZoneCapturePanel;
            UpdateMousePosition(e); ToolsPanelShowButton.Focus();
            zoneConsumedButtons |= e.Button;
            if (e.Button == MouseButtons.Right)
            {
                if (panel.Drawing) { try { panel.FinishDrawing(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Zone polygon"); } }
                else { zoneOrbit = true; Capture = true; }
                return true;
            }
            if (e.Button != MouseButtons.Left) return true;
            lock (Renderer.RenderSyncRoot)
            {
                var ray = ZoneClickRay(e.X, e.Y);
                if (panel.Drawing)
                {
                    Vector3 point;
                    bool hit = panel.SurfaceClicks && (ModifierKeys & Keys.Alt) == 0 ? ZoneSurfaceHit(ray, out point) : ZonePlaneHit(ray, panel.PlaneZ, out point);
                    if (hit) panel.AddPoint(point); else panel.NoSurface();
                    return true;
                }
                if ((ModifierKeys & Keys.Control) != 0 && ZoneInsertOnEdge(e, ray)) return true;
                int index = ZonePickPoint(panel.Outline, e.X, e.Y); panel.SelectPoint(index);
                if (index >= 0)
                {
                    zoneDragStart = panel.Outline[index]; zoneDragOffset = Vector3.Zero;
                    if (ZonePlaneHit(ray, zoneDragStart.Z, out var hit)) zoneDragOffset = zoneDragStart - hit;
                    panel.BeginDrag(); zoneDragging = true; Capture = true;
                }
            }
            return true;
        }
        private bool ZoneCaptureMouseMove(MouseEventArgs e)
        {
            if (!ZoneInteraction) return false;
            int dx = e.X - MouseX, dy = e.Y - MouseY;
            UpdateMousePosition(e);
            if (zoneOrbit) { RotateCam(dx, MouseInvert ? -dy : dy); return true; }
            lock (Renderer.RenderSyncRoot)
            {
                if (zoneDragging)
                {
                    var ray = ZoneClickRay(e.X, e.Y);
                    if ((ModifierKeys & Keys.Shift) != 0)
                    { if (ZoneSurfaceHit(ray, out var surface)) ZoneCapturePanel.MovePoint(ZoneCapturePanel.SelectedPoint, surface); }
                    else if (ZonePlaneHit(ray, zoneDragStart.Z, out var plane)) ZoneCapturePanel.MovePoint(ZoneCapturePanel.SelectedPoint, plane + zoneDragOffset);
                    Cursor = Cursors.SizeAll;
                }
                else Cursor = ZoneCapturePanel.Drawing ? Cursors.Cross : ZonePickPoint(ZoneCapturePanel.Outline, e.X, e.Y) >= 0 ? Cursors.Hand : Cursors.Default;
            }
            return true;
        }
        private bool ZoneCaptureMouseUp(MouseEventArgs e)
        {
            if ((zoneConsumedButtons & e.Button) == 0 && !ZoneInteraction) return false;
            zoneConsumedButtons &= ~e.Button;
            if (e.Button == MouseButtons.Left) { zoneDragging = false; ZoneCapturePanel?.EndDrag(); }
            if (e.Button == MouseButtons.Right) zoneOrbit = false;
            if (!zoneDragging && !zoneOrbit) Capture = false;
            MouseLButtonDown = MouseRButtonDown = false; return true;
        }
        private bool ZoneCaptureKeyDown(KeyEventArgs e)
        {
            if (!ZoneInteraction) return false;
            var panel = ZoneCapturePanel;
            try
            {
                switch (e.KeyCode)
                {
                    case Keys.Enter: if (panel.Drawing) panel.FinishDrawing(); else panel.StopDrawing(); break;
                    case Keys.Escape:
                        if (zoneDragging) { zoneDragging = false; panel.EndDrag(true); Capture = false; }
                        else panel.StopDrawing();
                        break;
                    case Keys.Delete: if (!zoneDragging) panel.DeletePoint(); break;
                    case Keys.Back: if (!zoneDragging) panel.UndoPoint(); break;
                    case Keys.Z: if (e.Control) { if (!zoneDragging) panel.UndoPoint(); } else Input.KeyDown(e, true); break;
                    default: Input.KeyDown(e, true); break;
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Zone polygon"); }
            e.Handled = true; e.SuppressKeyPress = true; return true;
        }

        private void RenderZoneCapture()
        {
            var panel = ZoneCapturePanel;
            if (panel == null || panel.IsDisposed) return;
            var state = panel.Visual;
            Renderer.RenderZoneOverlay(state.Points, state.Fill, state.MinZ, state.MaxZ, state.Valid, state.Selected, panel.Interacting);
        }
    }
}
