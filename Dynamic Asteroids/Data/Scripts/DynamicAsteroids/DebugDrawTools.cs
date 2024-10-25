using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids
{
    public partial class MainSession
    {

        public override void Draw()
        {
            try
            {
                if (!AsteroidSettings.EnableLogging || MyAPIGateway.Session?.Player?.Character == null)
                    return;

                Vector3D characterPosition = MyAPIGateway.Session.Player.Character.PositionComp.GetPosition();
                DrawPlayerZones(characterPosition);
                DrawNearestAsteroidDebug(characterPosition);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, typeof(MainSession), "Error in Draw");
            }
        }

        private void DrawPlayerZones(Vector3D characterPosition)
        {
            foreach (var kvp in _clientZones)
            {
                DrawZone(kvp.Key, kvp.Value, characterPosition);
                if (kvp.Value.IsMerged)
                {
                    DrawZoneMergeConnections(kvp.Value);
                }
            }
        }

        private void DrawZone(long playerId, AsteroidZone zone, Vector3D characterPosition)
        {
            bool isLocalPlayer = playerId == MyAPIGateway.Session.Player.IdentityId;
            bool playerInZone = zone.IsPointInZone(characterPosition);

            Color zoneColor = DetermineZoneColor(isLocalPlayer, playerInZone, zone.IsMerged);

            DrawZoneSphere(zone, zoneColor);
            DrawZoneInfo(zone, isLocalPlayer, playerInZone);
        }

        private Color DetermineZoneColor(bool isLocalPlayer, bool playerInZone, bool isMerged)
        {
            if (isLocalPlayer)
                return playerInZone ? Color.Green : Color.Yellow;
            return isMerged ? Color.Purple : Color.Blue;
        }

        private void DrawZoneSphere(AsteroidZone zone, Color color)
        {
            MatrixD worldMatrix = MatrixD.CreateTranslation(zone.Center);

            // Draw wireframe
            MySimpleObjectDraw.DrawTransparentSphere(
                ref worldMatrix,
                (float)zone.Radius,
                ref color,
                MySimpleObjectRasterizer.Wireframe,
                20,
                null,
                MyStringId.GetOrCompute("Square"),
                5f);

            // Draw filled sphere
            Color fillColor = new Color(color.R, color.G, color.B, 10);
            MySimpleObjectDraw.DrawTransparentSphere(
                ref worldMatrix,
                (float)zone.Radius,
                ref fillColor,
                MySimpleObjectRasterizer.Wireframe,
                20,
                null,
                MyStringId.GetOrCompute("Square"),
                5f);
        }

        private void DrawZoneMergeConnections(AsteroidZone sourceZone)
        {
            foreach (var targetZone in _clientZones.Values)
            {
                if (targetZone.IsMerged && targetZone != sourceZone)
                {
                    double distance = Vector3D.Distance(sourceZone.Center, targetZone.Center);
                    if (distance <= sourceZone.Radius + targetZone.Radius)
                    {
                        Vector4 mergeLineColor = Color.Purple.ToVector4();
                        MySimpleObjectDraw.DrawLine(
                            sourceZone.Center,
                            targetZone.Center,
                            MyStringId.GetOrCompute("Square"),
                            ref mergeLineColor,
                            2f);
                    }
                }
            }
        }

        private void DrawZoneInfo(AsteroidZone zone, bool isLocalPlayer, bool playerInZone)
        {
            Vector3D textPosition = zone.Center + new Vector3D(0, zone.Radius + 100, 0);

            // Fix the billboard drawing
            MyTransparentGeometry.AddLineBillboard(
                MyStringId.GetOrCompute("Square"),
                Color.White,
                textPosition,
                Vector3.Right,
                (float)zone.Radius * 0.1f,
                0.5f,
                MyBillboard.BlendTypeEnum.Standard);

            //if (isLocalPlayer)
            //{
            //    MyAPIGateway.Utilities.ShowNotification(
            //        $"Your Zone Status:\n" +
            //        $"In Zone: {playerInZone}\n" +
            //        $"Distance: {Vector3D.Distance(MyAPIGateway.Session.Player.GetPosition(), zone.Center):F0}m\n" +
            //        $"Merged: {zone.IsMerged}",
            //        16);
            //}
        }

        private void DrawNearestAsteroidDebug(Vector3D characterPosition)
        {
            AsteroidEntity nearestAsteroid = FindNearestAsteroid(characterPosition);
            if (nearestAsteroid == null) return;

            DrawAsteroidClientPosition(nearestAsteroid);
            DrawAsteroidServerComparison(nearestAsteroid);
        }

        private void DrawAsteroidClientPosition(AsteroidEntity asteroid)
        {
            Vector3D clientPosition = asteroid.PositionComp.GetPosition();
            MatrixD clientWorldMatrix = MatrixD.CreateTranslation(clientPosition);
            Color clientColor = Color.Red;
            MySimpleObjectDraw.DrawTransparentSphere(
                ref clientWorldMatrix,
                asteroid.Properties.Radius,
                ref clientColor,
                MySimpleObjectRasterizer.Wireframe,
                20);
        }

        private void DrawAsteroidServerComparison(AsteroidEntity asteroid)
        {
            Vector3D serverPosition;
            Quaternion serverRotation;

            if (!_serverPositions.TryGetValue(asteroid.EntityId, out serverPosition) ||
                !_serverRotations.TryGetValue(asteroid.EntityId, out serverRotation))
                return;

            DrawServerPositionSphere(asteroid, serverPosition);
            DrawPositionComparisonLine(asteroid.PositionComp.GetPosition(), serverPosition);
            DrawRotationComparison(asteroid, serverPosition, serverRotation);
            DisplayAsteroidDebugInfo(asteroid, serverPosition, serverRotation);
        }

        private void DrawServerPositionSphere(AsteroidEntity asteroid, Vector3D serverPosition)
        {
            MatrixD serverWorldMatrix = MatrixD.CreateTranslation(serverPosition);
            Color serverColor = Color.Blue;
            MySimpleObjectDraw.DrawTransparentSphere(
                ref serverWorldMatrix,
                asteroid.Properties.Radius,
                ref serverColor,
                MySimpleObjectRasterizer.Wireframe,
                20);
        }

        private void DrawPositionComparisonLine(Vector3D clientPosition, Vector3D serverPosition)
        {
            Vector4 lineColor = Color.Yellow.ToVector4();
            MySimpleObjectDraw.DrawLine(
                clientPosition,
                serverPosition,
                MyStringId.GetOrCompute("Square"),
                ref lineColor,
                0.1f);
        }

        private void DrawRotationComparison(AsteroidEntity asteroid, Vector3D serverPosition, Quaternion serverRotation)
        {
            Vector3D clientForward = asteroid.WorldMatrix.Forward;
            Vector3D serverForward = MatrixD.CreateFromQuaternion(serverRotation).Forward;
            Vector3D clientPosition = asteroid.PositionComp.GetPosition();

            Vector4 clientDirColor = Color.Red.ToVector4();
            Vector4 serverDirColor = Color.Blue.ToVector4();

            MySimpleObjectDraw.DrawLine(
                clientPosition,
                clientPosition + clientForward * asteroid.Properties.Radius * 2,
                MyStringId.GetOrCompute("Square"),
                ref clientDirColor,
                0.1f);

            MySimpleObjectDraw.DrawLine(
                serverPosition,
                serverPosition + serverForward * asteroid.Properties.Radius * 2,
                MyStringId.GetOrCompute("Square"),
                ref serverDirColor,
                0.1f);
        }

        private void DisplayAsteroidDebugInfo(AsteroidEntity asteroid, Vector3D serverPosition, Quaternion serverRotation)
        {
            float angleDifference = GetQuaternionAngleDifference(
                Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix),
                serverRotation);

            MyAPIGateway.Utilities.ShowNotification(
                $"Asteroid {asteroid.EntityId}:\n" +
                $"Position diff: {Vector3D.Distance(asteroid.PositionComp.GetPosition(), serverPosition):F2}m\n" +
                $"Rotation diff: {MathHelper.ToDegrees(angleDifference):F1}°",
                16);
        }

    }
}
