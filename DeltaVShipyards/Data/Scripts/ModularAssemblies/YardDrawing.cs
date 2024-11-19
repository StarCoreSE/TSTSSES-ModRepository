using System.Collections.Generic;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using Sandbox.ModAPI;
using VRageRender;
using VRage.Game;
using System;
using Sandbox.Game;
using VRage.Utils;

namespace Scripts.ModularAssemblies
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class YardDrawing : MySessionComponentBase
    {
        private Dictionary<int, BoundingBoxD> _yardBoxes = new Dictionary<int, BoundingBoxD>();
        private bool _isInitialized;
        public static YardDrawing Instance { get; private set; }

        public override void LoadData()
        {
            if (_isInitialized)
                return;

            Instance = this;
            _isInitialized = true;
        }

        protected override void UnloadData()
        {
            _yardBoxes.Clear();
            Instance = null;
            _isInitialized = false;
        }

        public void UpdateYardBox(int assemblyId, List<IMyCubeBlock> corners)
        {
            if (!_isInitialized || MyAPIGateway.Session?.Camera == null)
                return;

            if (corners.Count < 8)
            {
                _yardBoxes.Remove(assemblyId);
                return;
            }

            Vector3D min = Vector3D.MaxValue;
            Vector3D max = Vector3D.MinValue;

            foreach (var corner in corners)
            {
                Vector3D pos = corner.GetPosition();
                min = Vector3D.Min(min, pos);
                max = Vector3D.Max(max, pos);
            }

            // Expand the box slightly for visual clarity
            Vector3D expansion = (max - min) * 0.05;
            _yardBoxes[assemblyId] = new BoundingBoxD(min - expansion, max + expansion);
        }

        public override void Draw()
        {
            try
            {
                if (!_isInitialized || MyAPIGateway.Session?.Camera == null || MyAPIGateway.Utilities.IsDedicated)
                    return;

                foreach (var kvp in _yardBoxes)
                {
                    BoundingBoxD box = kvp.Value;
                    Vector3D center = box.Center;

                    // Only draw if within reasonable distance from camera
                    if (Vector3D.DistanceSquared(center, MyAPIGateway.Session.Camera.Position) > 1000 * 1000)
                        continue;

                    MatrixD worldMatrix = MatrixD.CreateWorld(center, Vector3D.Forward, Vector3D.Up);
                    Color boxColor = Color.Cyan * 0.5f;

                    MySimpleObjectDraw.DrawTransparentBox(
                        ref worldMatrix,
                        ref box,
                        ref boxColor,
                        MySimpleObjectRasterizer.SolidAndWireframe,
                        1,
                        0.02f,
                        null,
                        null,
                        false
                    );

                    // Add debug effects like the example mod
                    MyVisualScriptLogicProvider.ShowNotification($"Drawing yard box at {center}", 16);
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"YardDrawing.Draw: {e}");
            }
        }

        public bool IsPointInYard(Vector3D point, out int yardId)
        {
            foreach (var kvp in _yardBoxes)
            {
                if (kvp.Value.Contains(point) == ContainmentType.Contains)
                {
                    yardId = kvp.Key;
                    return true;
                }
            }

            yardId = -1;
            return false;
        }
    }
}