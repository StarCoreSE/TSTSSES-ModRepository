﻿using DynamicAsteroids.Data.Scripts.DynamicAsteroids.AsteroidEntities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace DynamicAsteroids.Data.Scripts.DynamicAsteroids {
    public partial class MainSession {
        private Queue<AsteroidZone> _lastRemovedZones = new Queue<AsteroidZone>(5); // Add this to class fields
        private HashSet<AsteroidEntity> _orphanedAsteroids = new HashSet<AsteroidEntity>();
        private int _orphanCheckTimer = 0;
        private const int ORPHAN_CHECK_INTERVAL = 60; // Update once per second at 60 fps

        public override void Draw() {
            try {
                if (!AsteroidSettings.EnableLogging || MyAPIGateway.Session?.Player?.Character == null)
                    return;

                Vector3D characterPosition = MyAPIGateway.Session.Player.Character.PositionComp.GetPosition();
                DrawPlayerZones(characterPosition);
                DrawNearestAsteroidDebug(characterPosition);
                DrawOrphanedAsteroids();
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), "Error in Draw");
            }
        }


        private void DrawPlayerZones(Vector3D characterPosition) {
            // Draw active zones first
            foreach (var kvp in _clientZones) {
                DrawZone(kvp.Key, kvp.Value, characterPosition);
                if (kvp.Value.IsMerged) {
                    DrawZoneMergeConnections(kvp.Value);
                }
            }

            // Draw recently removed zones with fading effect
            foreach (var removedZone in _lastRemovedZones) {
                Color fadeColor = Color.Red * 0.5f; // Semi-transparent red for removed zones
                DrawZoneSphere(removedZone, fadeColor);
            }
        }

        private void DrawZone(long playerId, AsteroidZone zone, Vector3D characterPosition) {
            bool isLocalPlayer = playerId == MyAPIGateway.Session.Player.IdentityId;
            bool playerInZone = zone.IsPointInZone(characterPosition);

            Color zoneColor = DetermineZoneColor(isLocalPlayer, playerInZone, zone.IsMerged);

            DrawZoneSphere(zone, zoneColor);
            DrawZoneInfo(zone, isLocalPlayer, playerInZone);
        }

        private void DrawZoneInfo(AsteroidZone zone, bool isLocalPlayer, bool playerInZone) {
            Vector3D textPosition = zone.Center + new Vector3D(0, zone.Radius + 100, 0);

            // Create info string including new states
            string zoneInfo = $"Asteroids: {zone.ContainedAsteroids?.Count ?? 0}\n" +
                              $"Active: {!zone.IsMarkedForRemoval}\n" +
                              $"Last Active: {(DateTime.UtcNow - zone.LastActiveTime).TotalSeconds:F1}s ago";

            // Draw zone status text
            MyTransparentGeometry.AddLineBillboard(
                MyStringId.GetOrCompute("Square"),
                Color.White,
                textPosition,
                Vector3.Right,
                (float)zone.Radius * 0.1f,
                0.5f,
                MyBillboard.BlendTypeEnum.Standard
            );
        }

        private Color DetermineZoneColor(bool isLocalPlayer, bool playerInZone, bool isMerged) {
            // Handle high-speed check through the AsteroidZone properties instead
            if (isLocalPlayer && MyAPIGateway.Session?.Player != null) {
                AsteroidZone currentZone;
                if (_clientZones.TryGetValue(MyAPIGateway.Session.Player.IdentityId, out currentZone)) {
                    if (currentZone.IsMarkedForRemoval)
                        return Color.Red;
                }
            }

            // Use zone state for coloring
            if (isLocalPlayer)
                return playerInZone ? Color.Green : Color.Yellow;

            if (isMerged)
                return Color.Purple;

            return Color.Blue;
        }

        private void DrawZoneSphere(AsteroidZone zone, Color color) {
            MatrixD worldMatrix = MatrixD.CreateTranslation(zone.Center);
            MySimpleObjectDraw.DrawTransparentSphere(
                ref worldMatrix,
                (float)zone.Radius,
                ref color,
                MySimpleObjectRasterizer.Wireframe,
                20,
                null,
                MyStringId.GetOrCompute("Square"),
                5f
            );
        }

        private void DrawZoneMergeConnections(AsteroidZone sourceZone) {
            foreach (var targetZone in _clientZones.Values) {
                if (targetZone.IsMerged && targetZone != sourceZone) {
                    double distance = Vector3D.Distance(sourceZone.Center, targetZone.Center);
                    if (distance <= sourceZone.Radius + targetZone.Radius) {
                        // Red lines for connections involving marked-for-removal zones
                        Color connectionColor = (sourceZone.IsMarkedForRemoval || targetZone.IsMarkedForRemoval)
                            ? Color.Red
                            : Color.Purple;

                        Vector4 mergeLineColor = connectionColor.ToVector4();
                        MySimpleObjectDraw.DrawLine(
                            sourceZone.Center,
                            targetZone.Center,
                            MyStringId.GetOrCompute("Square"),
                            ref mergeLineColor,
                            2f
                        );
                    }
                }
            }
        }
       
        private void DrawNearestAsteroidDebug(Vector3D characterPosition) {
            AsteroidEntity nearestAsteroid = FindNearestAsteroid(characterPosition);
            if (nearestAsteroid == null) return;

            DrawAsteroidClientPosition(nearestAsteroid);
            DrawAsteroidServerComparison(nearestAsteroid);
        }

        private void DrawAsteroidClientPosition(AsteroidEntity asteroid) {
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

        private void DrawAsteroidServerComparison(AsteroidEntity asteroid) {
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

        private void DrawServerPositionSphere(AsteroidEntity asteroid, Vector3D serverPosition) {
            MatrixD serverWorldMatrix = MatrixD.CreateTranslation(serverPosition);
            Color serverColor = Color.Blue;
            MySimpleObjectDraw.DrawTransparentSphere(
                ref serverWorldMatrix,
                asteroid.Properties.Radius,
                ref serverColor,
                MySimpleObjectRasterizer.Wireframe,
                20);
        }

        private void DrawPositionComparisonLine(Vector3D clientPosition, Vector3D serverPosition) {
            Vector4 lineColor = Color.Yellow.ToVector4();
            MySimpleObjectDraw.DrawLine(
                clientPosition,
                serverPosition,
                MyStringId.GetOrCompute("Square"),
                ref lineColor,
                0.1f);
        }

        private void DrawRotationComparison(AsteroidEntity asteroid, Vector3D serverPosition, Quaternion serverRotation) {
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

        private void DisplayAsteroidDebugInfo(AsteroidEntity asteroid, Vector3D serverPosition, Quaternion serverRotation) {
            float angleDifference = GetQuaternionAngleDifference(
                Quaternion.CreateFromRotationMatrix(asteroid.WorldMatrix),
                serverRotation);

            MyAPIGateway.Utilities.ShowNotification(
                $"Asteroid {asteroid.EntityId}:\n" +
                $"Position diff: {Vector3D.Distance(asteroid.PositionComp.GetPosition(), serverPosition):F2}m\n" +
                $"Rotation diff: {MathHelper.ToDegrees(angleDifference):F1}°",
                16);
        }

        private void UpdateOrphanedAsteroidsList() {
            try {
                _orphanedAsteroids.Clear();
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities) {
                    var asteroid = entity as AsteroidEntity;
                    if (asteroid == null)
                        continue;

                    Vector3D asteroidPosition = asteroid.PositionComp.GetPosition();
                    bool isInAnyZone = false;

                    foreach (var zoneKvp in _clientZones) {
                        if (zoneKvp.Value.IsPointInZone(asteroidPosition)) {
                            isInAnyZone = true;
                            break;
                        }
                    }

                    if (!isInAnyZone) {
                        _orphanedAsteroids.Add(asteroid);
                    }
                }
            }
            catch (Exception ex) {
                Log.Exception(ex, typeof(MainSession), "Error updating orphaned asteroids list");
            }
        }

        private void DrawOrphanedAsteroids() {
            foreach (var asteroid in _orphanedAsteroids) {
                if (asteroid == null || asteroid.MarkedForClose)
                    continue;

                try {
                    Vector3D asteroidPosition = asteroid.PositionComp.GetPosition();
                    MatrixD worldMatrix = MatrixD.CreateTranslation(asteroidPosition);
                    Color orphanColor = new Color(255, 0, 0, 128);

                    MySimpleObjectDraw.DrawTransparentSphere(
                        ref worldMatrix,
                        asteroid.Properties.Radius * 1.5f,
                        ref orphanColor,
                        MySimpleObjectRasterizer.Wireframe,
                        20,
                        null,
                        MyStringId.GetOrCompute("Square"),
                        5f
                    );

                    Vector3D textPosition = asteroidPosition + new Vector3D(0, asteroid.Properties.Radius * 2, 0);
                    string orphanInfo = $"Orphaned Asteroid {asteroid.EntityId}\nType: {asteroid.Type}";
                    MyTransparentGeometry.AddLineBillboard(
                        MyStringId.GetOrCompute("Square"),
                        Color.Red,
                        textPosition,
                        Vector3.Right,
                        asteroid.Properties.Radius * 0.2f,
                        0.5f,
                        MyBillboard.BlendTypeEnum.Standard
                    );
                }
                catch (Exception ex) {
                    _orphanedAsteroids.Remove(asteroid);
                    Log.Warning($"Error drawing orphaned asteroid {asteroid.EntityId}: {ex.Message}");
                }
            }
        }
    }
}