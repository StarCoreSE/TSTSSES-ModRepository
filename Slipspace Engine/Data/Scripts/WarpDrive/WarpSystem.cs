using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GUI;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;
using Color = VRageMath.Color;

namespace WarpDriveMod
{
    public class WarpSystem
    {
        public bool Valid => grid.Valid;
        public long InvalidOn => grid.InvalidOn;
        public int Id { get; private set; }
        public State WarpState { get; set; }
        public static WarpSystem Instance;
        public event Action<WarpSystem> OnSystemInvalidatedAction;
        public List<IMyPlayer> OnlinePlayersList = new List<IMyPlayer>();
        public double currentSpeedPt = WarpDrive.Instance.Settings.startSpeed;
        public int DriveHeat { get; set; }
        public GridSystem grid;
        public bool ProxymityStop = false;
        public bool SafeTriggerON = false;

        #region +++ newVariables        
        private List<IMyCubeGrid> allAttachedGrids = new List<IMyCubeGrid>();           // All attached grids
        private List<MyEntitySubpart> physicParts = new List<MyEntitySubpart>();        // All subparts from fatblocks
        private List<MechItem> mechConnections = new List<MechItem>();                  // All rotors/hinges
        private List<MyGyro> steeringGyros = new List<MyGyro>();                        // All gyros from all connected grids
        private int lastJump = 0;
        private long startingDelay = 0;
        public IMyShipController controlledCockpit { get; private set; } = null;        // Helmsman needed for mixed grid steering
        private bool gotTeleported = false;
        private bool fixPhysics = false;
        private double pitch = 0d;
        private double yaw = 0d;
        private double roll = 0d;
        private double gridRadius = 0d;
        #endregion

        public MatrixD gridMatrix;
        private readonly Dictionary<IMyCubeGrid, HashSet<WarpDrive>> warpDrives = new Dictionary<IMyCubeGrid, HashSet<WarpDrive>>();
        private readonly List<IMyPlayer> PlayersInWarpList = new List<IMyPlayer>();
        private readonly List<IMyFunctionalBlock> TempDisabledDrives = new List<IMyFunctionalBlock>();
        public readonly Dictionary<long, float> GridsMass = new Dictionary<long, float>();
        public readonly Dictionary<long, Vector3D> GridSpeedLinearVelocity = new Dictionary<long, Vector3D>();
        public readonly Dictionary<long, Vector3D> GridSpeedAngularVelocity = new Dictionary<long, Vector3D>();
        private MyParticleEffect effect;
        private readonly MyEntity3DSoundEmitter sound;
        public MyParticleEffect BlinkTrailEffect;
        private long startChargeRuntime = -1;
        private bool hasEnoughPower = true;
        private int functionalDrives;
        private IMyCubeGrid startWarpSource;
        private float totalHeat = 0;
        private int _updateTicks = 0;
        private int UpdatePlayersTick = 0;
        private int BlockOnTick = 0;
        private int PowerCheckTick = 0;
        private int MassUpdateTick = 0;
        private int MassChargeUpdate = 180;
        private bool TeleportNow = false;
        private bool WarpDropSound = false;
        private int timeInWarpCounter = 0;

        public bool IsPrototech = false;

        public string warnDestablalized = "Slipspace destabilized!";
        public string warnAborted = "Charging procedure aborted!";
        public string warnDamaged = "Frame shift drive Offline or Damaged!";
        public string warnNoPower = "Not enough power!";
        public string TooFast = "Decrease your speed!";
        public string EmergencyDropSpeed = "Emergency Stop!";
        public string warnStatic = "Unable to move static grid!";
        public string warnInUse = "Grid is already at Slipspace!";
        public string warnNoEstablish = "Unable to establish Slipspace!";
        public string warnOverheat = "Frame shift drive overheated!";
        //public string ProximytyAlert = "Can't Start FSD, Proximity Alert!";
        public string ProximityChargeAlert = "Proximity Alert! Disrupted by "; // added by GetFriendlyName()
        public string warnMainCockpit = "Main cockpit set but not used!";
        public string warnCockpit = "No cockpit found, supercruise aborted!";
        public string warnConnectorYellow = "Unable to establish supercruise (Code: Yellow)";

        public const float EARTH_GRAVITY = 9.806652f;

        public WarpSystem(WarpDrive block, WarpSystem oldSystem)
        {
            if (block.Block.BlockDefinition.SubtypeId == "PrototechSlipspaceCoreLarge" || block.Block.BlockDefinition.SubtypeId == "PrototechSlipspaceCoreSmall")
                IsPrototech = true;

            if (block == null || block.Block == null || block.Block.CubeGrid == null)
                return;

            Id = WarpDriveSession.Instance.Rand.Next(int.MinValue, int.MaxValue);

            grid = new GridSystem((MyCubeGrid)block.Block.CubeGrid);

            GridSystem.BlockCounter warpDriveCounter = new GridSystem.BlockCounter((b) => b?.GameLogic.GetAs<WarpDrive>() != null);
            warpDriveCounter.OnBlockAdded += OnDriveAdded;
            warpDriveCounter.OnBlockRemoved += OnDriveRemoved;
            grid.AddCounter("WarpDrives", warpDriveCounter);

            grid.OnSystemInvalidated += OnSystemInvalidated;

            if (!MyAPIGateway.Utilities.IsDedicated && grid.MainGrid != null)
            {
                sound = new MyEntity3DSoundEmitter(grid.MainGrid)
                {
                    CanPlayLoopSounds = true
                };
            }

            if (oldSystem != null)
            {
                startWarpSource = oldSystem.startWarpSource;
                if (startWarpSource?.MarkedForClose == true)
                    startWarpSource = null;

                totalHeat = oldSystem.totalHeat;
                WarpState = oldSystem.WarpState;

                if (WarpState == State.Charging)
                {

                    if (!MyAPIGateway.Utilities.IsDedicated)
                    {
                        try
                        {
                            PlayParticleEffect();
                        }
                        catch { }
                    };

                    startChargeRuntime = oldSystem.startChargeRuntime;
                    WarpState = State.Charging;
                }
                else if (WarpState == State.Active)
                {
                    currentSpeedPt = oldSystem.currentSpeedPt;
                    WarpState = State.Active;
                }
            }

            block.SetWarpSystem(this);
        }

        private void UpdateOnlinePlayers()
        {
            OnlinePlayersList.Clear();
            MyAPIGateway.Players.GetPlayers(OnlinePlayersList);
        }

        public void UpdateBeforeSimulation()
        {
            #region prepare
            if (Instance == null)
                Instance = this;

            if (WarpDriveSession.Instance == null || WarpDrive.Instance == null || WarpDrive.Instance.Settings == null || grid == null || grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;

            if (UpdatePlayersTick++ >= 300)
            {
                UpdateOnlinePlayers();
                UpdatePlayersTick = 0;
            }

            if (BlockOnTick++ >= 60)
            {
                BlockOnTick = 0;

                if (TempDisabledDrives.Count > 0)
                {
                    foreach (var block in TempDisabledDrives)
                    {
                        if (block != null)
                            block.Enabled = true;
                    }
                    TempDisabledDrives.Clear();
                }
            }

            if (warpDrives.Count == 0)
                grid.Invalidate();
            #endregion

            UpdateHeatPower();

            if (WarpState == State.Charging || WarpState == State.Active)
            {
                gridMatrix = grid.FindWorldMatrix();
                controlledCockpit = grid.FindMainCockpit();
            }

            /*if (WarpState == State.Charging || WarpState == State.Active)
            {
                collisionDEBUGGING();
                //collisionDEBUGGING_onCharge();
            }*/


            if (WarpState == State.Charging)
                InCharge();

            if (WarpState == State.Active)
            {
                if (!MyAPIGateway.Utilities.IsDedicated)
                    sound.SetPosition(MainGrid.PositionComp.GetPosition());

                if (InWarp())
                    TeleportNow = true;

                // more particles
                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    if (currentSpeedPt < 316.6666d)
                    {
                        // DrawAllLines();
                        DrawAllLinesCenter2();
                        DrawAllLinesCenter3();

                        if (WarpDropSound)
                        {
                            if (IsPrototech)
                            {
                                sound.PlaySound(WarpConstants.PrototechJumpOutSound, true);
                                sound.VolumeMultiplier = 1;
                            }
                            else
                            {
                                sound.PlaySound(WarpConstants.jumpOutSound, true);
                                sound.VolumeMultiplier = 1;
                            }
                            WarpDropSound = false;
                        }
                    }
                    else
                        DrawAllLinesCenter2();
                }
            }

            // regular FSD moving and particle drawing for client            
            if (TeleportNow && !SafeTriggerON)
            {
                TeleportNow = false;

                if (currentSpeedPt > 1d && gridMatrix != null && controlledCockpit != null)
                {
                    // Rotation around this axis by this angle
                    // center is controlled cockpit (that's vanialla)
                    MatrixD myMatrix = MainGrid.PositionComp.WorldMatrixRef;
                    MatrixD mylocalMatrix = MainGrid.PositionComp.WorldMatrixInvScaled;

                    // Do we need a stable translation multiplicator or Math.round() ??
                    //currentSpeedPt = (double)currentSpeedPt;

                    Vector3D forward = controlledCockpit.WorldMatrix.Forward;
                    Vector3D down = controlledCockpit.WorldMatrix.Down;
                    Vector3D left = controlledCockpit.WorldMatrix.Left;
                    Vector3D backward = controlledCockpit.WorldMatrix.Backward;

                    // Lets wait 10 ticks to prevent Connector / Rotor physic errors (physic needs some ticks)
                    startingDelay++;

                    // Grids new position    
                    Vector3D newPosition = myMatrix.Translation + (forward * (startingDelay < 10 ? 0d : currentSpeedPt));

                    // Remove Translation to prepare for rotation
                    myMatrix.Translation = Vector3D.Zero;

                    // Combined rotations, less floating errors
                    MatrixD rotationMatrix = MatrixD.CreateFromAxisAngle(down, MathHelper.ToRadians(startingDelay < 10 ? 0d : yaw)) *      //         left+ || -right
                                             MatrixD.CreateFromAxisAngle(left, MathHelper.ToRadians(startingDelay < 10 ? 0d : pitch)) *    //           up+ ||  -down
                                             MatrixD.CreateFromAxisAngle(backward, MathHelper.ToRadians(startingDelay < 10 ? 0d : roll));  // rotate  left+ || -right

                    // Apply rotation
                    myMatrix *= rotationMatrix;

                    // Apply translation (move forward)
                    myMatrix.Translation = Vector3D.Add(myMatrix.Translation, newPosition);

                    // FIRST: Set new subgrid positions 
                    foreach (IMyCubeGrid myGrid in allAttachedGrids)
                    {
                        if (myGrid == MainGrid)
                            continue;

                        // Get rid of floating errors for subgrids by normalization
                        MatrixD myNewSubgridMatrix = MatrixD.Normalize(myGrid.PositionComp.WorldMatrixRef * mylocalMatrix * myMatrix);
                        myGrid.PositionComp.SetWorldMatrix(ref myNewSubgridMatrix, myGrid, false, true, true, true, false, false);
                    }

                    // Maingrid with normalization
                    myMatrix = MatrixD.Normalize(myMatrix);

                    // LAST: Set newMainGrid position
                    MainGrid.PositionComp.SetWorldMatrix(ref myMatrix, MainGrid, false, true, true, true, false, false);

                    // ToDo: Drawing messing up matrix.... but we need it
                    gridMatrix = grid.FindWorldMatrix();


                    #region+++ added
                    gotTeleported = true;
                    fixPhysics = false; // fixed with gotTeleproted = true;
                    #endregion

                    if (!MyAPIGateway.Utilities.IsDedicated)
                    {
                        DrawAllLinesCenter1();

                        if (currentSpeedPt > 316.6666d)
                        {
                            //StartBlinkParticleEffect();
                            DrawAllLinesCenter4();
                        }
                    }
                }
            }

            #region +++rePhysic
            // (gotTeleported && lastJump > 0) - regular exit
            // SafeTriggerON - 
            // fixPhysics - true if we have no gridMove but cancel warp start
            if ((gotTeleported && lastJump > 0) || SafeTriggerON || fixPhysics)
            {

                // We need to start physics from core grid 
                MainGrid.Physics.Enabled = true;
                MainGrid.Physics.Clear();
                MainGrid.StopPhysicsActivation = true;

                allAttachedGrids.Reverse();
                foreach (IMyCubeGrid myGrid in allAttachedGrids)
                {
                    if (MainGrid == myGrid)
                        continue;

                    myGrid.Physics.Enabled = true;
                    myGrid.Physics.Clear();
                    myGrid.StopPhysicsActivation = true;
                }

                // reEnable physics of parts, could be 1000+ but UNABLE todo in parallel
                physicParts.Reverse();
                foreach (MyEntitySubpart p in physicParts)
                {
                    p.Physics.Enabled = true;
                    p.Physics.Clear();
                    p.StopPhysicsActivation = true;
                }

                // reAttach mechLinks, no group update!
                foreach (MechItem item in mechConnections)
                    item.Attach(false);

                // We need to look for position after last change
                // and teleport for ALL involved clients + server to get physic back to work
                MainGrid.Teleport(MatrixD.Normalize(grid.FindWorldMatrix()));

                // Set starting speed on regular exit
                if (lastJump == 2 && !SafeTriggerON)
                {
                    if (MainGrid.Physics != null && GridSpeedLinearVelocity.ContainsKey(MainGrid.EntityId))
                    {
                        MainGrid.Physics.LinearVelocity = GridSpeedLinearVelocity[MainGrid.EntityId];
                        MainGrid.Physics.AngularVelocity = GridSpeedAngularVelocity[MainGrid.EntityId];
                    }
                }

                // Show vaniall horizon etc
                updateMyShowHorizonIndicator(true);

                // Cleanup
                clear_all_Lists();
                //controlledCockpit = null;
                gotTeleported = false;
                fixPhysics = false;
                startingDelay = 0;
                lastJump = 0;
                pitch = 0d;
                yaw = 0d;
                roll = 0d;
                gridRadius = 0d;
            }
            #endregion
        }

        private void clear_all_Lists()
        {
            physicParts.Clear();
            steeringGyros.Clear();
            allAttachedGrids.Clear();
            mechConnections.Clear();
        }

        private bool InWarp()
        {
            if (grid.MainGrid == null)
                return false;

            var MainGrid = grid.MainGrid;
            var WarpDriveOnGrid = GetActiveWarpDrive(MainGrid);

            if (PlayersInWarpList.Count > 0)
            {
                foreach (var Player in PlayersInWarpList)
                {
                    if (Player == null || Player.Character == null)
                        continue;

                    if (Player.Character.Save)
                        Player.Character.Save = false;
                }
            }

            if (IsInGravity())
            {
                SendMessage(warnDestablalized);

                if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                    Dewarp(true);
                else
                    Dewarp();

                return false;
            }
            else
                updateMyShowHorizonIndicator(false);

            string msg = WarpDrive.Instance.ProxymityDangerInWarp(gridMatrix, MainGrid, gridRadius, currentSpeedPt, controlledCockpit);
            if (msg != null)
            {
                currentSpeedPt = -1d;

                if (!MyAPIGateway.Utilities.IsDedicated)
                    ProxymityStop = true;

                SendMessage(EmergencyDropSpeed);

                // true here for ship speed to 0! collision detected.
                Dewarp(true);

                if (WarpDriveOnGrid != null)
                {
                    foreach (var ActiveDrive in GetActiveWarpDrives())
                    {
                        if (ActiveDrive.Enabled)
                        {
                            ActiveDrive.Enabled = false;
                            if (!TempDisabledDrives.Contains(ActiveDrive))
                                TempDisabledDrives.Add(ActiveDrive);
                        }
                    }
                }

                return false;
            }

            if (!hasEnoughPower)
            {
                SendMessage(warnNoPower);
                Dewarp();

                if (WarpDriveOnGrid != null)
                {
                    foreach (var ActiveDrive in GetActiveWarpDrives())
                    {
                        if (ActiveDrive.Enabled)
                        {
                            ActiveDrive.Enabled = false;
                            if (!TempDisabledDrives.Contains(ActiveDrive))
                                TempDisabledDrives.Add(ActiveDrive);
                        }
                    }
                }

                return false;
            }

            if (functionalDrives == 0)
            {
                SendMessage(warnDamaged);
                Dewarp();

                return false;
            }

            if (totalHeat >= WarpDrive.Instance.Settings.maxHeat)
            {
                SendMessage(warnOverheat);
                Dewarp();

                foreach (var ActiveDrive in GetActiveWarpDrives())
                {
                    if (ActiveDrive.Enabled)
                    {
                        ActiveDrive.Enabled = false;
                        if (!TempDisabledDrives.Contains(ActiveDrive))
                            TempDisabledDrives.Add(ActiveDrive);
                    }
                }

                return false;
            }

            if (MyAPIGateway.Utilities.IsDedicated && PlayersInWarpList.Count > 0)
            {
                var PlayerFound = false;
                foreach (var Player in PlayersInWarpList)
                {
                    if (OnlinePlayersList.Contains(Player))
                        PlayerFound = true;
                }

                if (!PlayerFound)
                {
                    // if player left server, stop warp and stop ship!
                    Dewarp(true);

                    foreach (var ActiveDrive in GetActiveWarpDrives())
                    {
                        if (ActiveDrive.Enabled)
                        {
                            ActiveDrive.Enabled = false;
                            if (!TempDisabledDrives.Contains(ActiveDrive))
                                TempDisabledDrives.Add(ActiveDrive);
                        }
                    }

                    return false;
                }
            }

            // Update Server/Client with WarpSpeed change.
            /*
             * Sollte beides deaktivieren (dann muss das auch von hand gelöst werden)
             * Künstlicher Horizont cockpit.ShowHorizonIndicator = false;
             * MyControlsSpace.???? >> SUPER-BOOST
             * Inertia-Damping by speed
             * Speed limit by planet range
             * Settings für Beschleunigung und Boosts
             * 
             * ... rewrite this to a propper expandable format...
             */
            #region Steering_LocalGame and Server
            if (!MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer)
            {
                var Hostplayer = MyAPIGateway.Session?.Player;
                var cockpit = Hostplayer?.Character?.Parent as IMyShipController;

                bool NotPressed_f = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.FORWARD);
                bool NotPressed_b = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.BACKWARD);
                bool boost = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.SPRINT);

                bool NotPressed_up = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROTATION_UP);
                bool NotPressed_down = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROTATION_DOWN);
                bool NotPressed_left = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROTATION_LEFT);
                bool NotPressed_right = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROTATION_RIGHT);
                bool NotPressed_q = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROLL_LEFT);
                bool NotPressed_e = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROLL_RIGHT);
                double mouse_x = MyAPIGateway.Input.GetMouseX();
                double mouse_y = MyAPIGateway.Input.GetMouseY();
                int mouse_cap = 40;


                // Fake physics, by hand
                double totalGyroForce = 0;
                double inertia = (2d / 5d) * (double)GridsMass.Values.Sum();

                foreach (MyGyro g in steeringGyros)
                    if (g.IsWorking)
                        totalGyroForce += (double)g.MaxGyroForce;

                double gyro_inertia = totalGyroForce / inertia;
                double max_angular_move = ((gyro_inertia * 1d) * Math.Pow(0.016, 2)) / speedDamper(currentSpeedPt);
                double cap_of_max_angular_move = 2.95d;

                bool allow_steering = !MyAPIGateway.Gui.IsCursorVisible && !MyAPIGateway.Gui.ChatEntryVisible;

                // Cap max_angular_move
                if (max_angular_move > cap_of_max_angular_move)
                    max_angular_move = cap_of_max_angular_move;

                // Cap gravity before
                if (WarpDriveOnGrid != null && WarpDriveSession.Instance.warpDrivesSpeeds.Count > 0)
                {
                    double[] movement;
                    double NewSpeed = 0d;
                    if (WarpDriveSession.Instance.warpDrivesSpeeds.TryGetValue(WarpDriveOnGrid, out movement))
                        NewSpeed = movement[0];

                    if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                    {
                        if (NewSpeed > WarpDrive.Instance.Settings.AllowInGravityMaxSpeed)
                        {
                            currentSpeedPt = 1000d / 60d;
                            WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] });
                        }
                        else
                            currentSpeedPt = NewSpeed;
                    }
                    else if (NewSpeed > WarpDrive.Instance.Settings.maxSpeed)
                    {
                        currentSpeedPt = WarpDrive.Instance.Settings.maxSpeed;
                        WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] });
                    }
                    else
                        currentSpeedPt = NewSpeed;
                }

                if (Hostplayer != null && cockpit?.CubeGrid != null && grid.Contains((MyCubeGrid)cockpit.CubeGrid))
                {
                    #region Steering (offline)                    
                    // Pitch
                    if (mouse_y != 0 && allow_steering)
                    {
                        if (mouse_y > mouse_cap)
                            mouse_y = mouse_cap;
                        else if (mouse_y < mouse_cap * -1)
                            mouse_y = mouse_cap * -1;

                        pitch = pitch + (max_angular_move / (20d * (mouse_cap / mouse_y)));
                    }
                    else if (!NotPressed_up && NotPressed_down && allow_steering)
                    {
                        // Down
                        if (pitch < 0d)
                            pitch = pitch + (max_angular_move / 20d);
                        else
                            pitch = pitch + (max_angular_move / 70d);
                    }
                    else if (NotPressed_up && !NotPressed_down && allow_steering)
                    {
                        // Up
                        if (pitch > 0d)
                            pitch = pitch - (max_angular_move / 20d);
                        else
                            pitch = pitch - (max_angular_move / 70d);
                    }
                    else
                    {
                        // Inertia - we modify old values to get a better feeling
                        if (pitch >= max_angular_move / 20d)
                            pitch = pitch - (max_angular_move / 20d);
                        else if (pitch <= max_angular_move / 20d * -1)
                            pitch = pitch + (max_angular_move / 20d);
                        else
                            pitch = 0d;
                    }
                    // Cap pitch to max_angular_move
                    if (pitch > cap_of_max_angular_move)
                        pitch = cap_of_max_angular_move;

                    if (pitch < (cap_of_max_angular_move * -1))
                        pitch = (cap_of_max_angular_move * -1);

                    // Yaw
                    if (mouse_x != 0 && allow_steering)
                    {
                        if (mouse_x > mouse_cap)
                            mouse_x = mouse_cap;
                        else if (mouse_x < mouse_cap * -1)
                            mouse_x = mouse_cap * -1;

                        yaw = yaw + (max_angular_move / (20d * (mouse_cap / mouse_x)));
                    }
                    else if (!NotPressed_left && NotPressed_right && allow_steering)
                    {
                        // 
                        if (yaw < 0d)
                            yaw = yaw + (max_angular_move / 20d);
                        else
                            yaw = yaw + (max_angular_move / 70d);
                    }
                    else if (NotPressed_left && !NotPressed_right && allow_steering)
                    {
                        // 
                        if (yaw > 0d)
                            yaw = yaw - (max_angular_move / 20d);
                        else
                            yaw = yaw - (max_angular_move / 70d);
                    }
                    else
                    {
                        // Inertia - we modify old values to get a better feeling
                        if (yaw >= max_angular_move / 20d)
                            yaw = yaw - (max_angular_move / 20d);
                        else if (yaw <= max_angular_move / 20d * -1)
                            yaw = yaw + (max_angular_move / 20d);
                        else
                            yaw = 0d;
                    }
                    // Cap yaw to max_angular_move
                    if (yaw > cap_of_max_angular_move)
                        yaw = cap_of_max_angular_move;

                    if (yaw < (cap_of_max_angular_move * -1))
                        yaw = (cap_of_max_angular_move * -1);

                    // Roll le/ri
                    if (!NotPressed_e && NotPressed_q && allow_steering)
                    {
                        // 
                        if (roll < 0d)
                            roll = roll + (max_angular_move / 20d);
                        else
                            roll = roll + (max_angular_move / 60d); // 70d
                    }
                    else if (NotPressed_e && !NotPressed_q && allow_steering)
                    {
                        // 
                        if (roll > 0d)
                            roll = roll - (max_angular_move / 20d);
                        else
                            roll = roll - (max_angular_move / 60d); // 70d
                    }
                    else
                    {
                        // Inertia - we modify old values to get a better feeling
                        if (roll >= max_angular_move / 20d)
                            roll = roll - (max_angular_move / 20d);
                        else if (roll <= max_angular_move / 20d * -1)
                            roll = roll + (max_angular_move / 20d);
                        else
                            roll = 0d;
                    }
                    // Cap roll to max_angular_move
                    if (roll > cap_of_max_angular_move)
                        roll = cap_of_max_angular_move;

                    if (roll < (cap_of_max_angular_move * -1))
                        roll = (cap_of_max_angular_move * -1);

                    // Faster
                    if (!NotPressed_b && NotPressed_f && allow_steering)
                    {
                        if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                        {
                            if (currentSpeedPt > WarpDrive.Instance.Settings.AllowInGravityMaxSpeed)
                            {
                                currentSpeedPt = WarpDrive.Instance.Settings.AllowInGravityMaxSpeed;

                                if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                    WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { WarpDrive.Instance.Settings.AllowInGravityMaxSpeed, pitch, yaw, roll }));
                                else
                                    WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { WarpDrive.Instance.Settings.AllowInGravityMaxSpeed, pitch, yaw, roll });
                            }
                            else
                            {
                                if (IsPrototech)
                                {
                                    currentSpeedPt += boost ? 3.0d : 1.5d;
                                }
                                else
                                {
                                    currentSpeedPt += boost ? 1.5d : 0.75d;
                                }

                                if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                    WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, pitch, yaw, roll }));
                                else
                                    WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, pitch, yaw, roll });
                            }
                        }
                        else if (currentSpeedPt > WarpDrive.Instance.Settings.maxSpeed)
                        {
                            currentSpeedPt = WarpDrive.Instance.Settings.maxSpeed;

                            if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { WarpDrive.Instance.Settings.maxSpeed, pitch, yaw, roll }));
                            else
                                WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { WarpDrive.Instance.Settings.maxSpeed, pitch, yaw, roll });
                        }
                        else
                        {
                            if (IsPrototech)
                            {
                                // ---- JUST 4 TESTING @ LOCAL
                                //currentSpeedPt += boost ? 3000000.0d : 1000.5d;
                                currentSpeedPt += boost ? 3.0d : 1.5d;
                            }
                            else
                            {
                                currentSpeedPt += boost ? 1.5d : 0.75d;
                            }

                            if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, pitch, yaw, roll }));
                            else
                                WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, pitch, yaw, roll });
                        }
                    }

                    // Slower
                    if (!NotPressed_f && NotPressed_b && allow_steering)
                    {
                        if (IsPrototech)
                        {
                            currentSpeedPt -= boost ? 3.0d : 1.5d;
                        }
                        else
                        {
                            currentSpeedPt -= boost ? 1.5d : 0.75d;
                        }
                        //also minimum speed
                        if (currentSpeedPt < 16d)
                            currentSpeedPt = -5d;

                        if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                            WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, pitch, yaw, roll }));
                        else
                            WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, pitch, yaw, roll });
                    }
                    #endregion
                    // Cap to gravity speed
                    if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                    {
                        if (currentSpeedPt > WarpDrive.Instance.Settings.AllowInGravityMaxSpeed)
                        {
                            currentSpeedPt = WarpDrive.Instance.Settings.AllowInGravityMaxSpeed;

                            if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { WarpDrive.Instance.Settings.AllowInGravityMaxSpeed, pitch, yaw, roll }));
                            else
                                WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { WarpDrive.Instance.Settings.AllowInGravityMaxSpeed, pitch, yaw, roll });
                        }
                    }
                    // Cap to max speed
                    else if (currentSpeedPt > WarpDrive.Instance.Settings.maxSpeed)
                    {
                        currentSpeedPt = WarpDrive.Instance.Settings.maxSpeed;

                        if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                            WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { WarpDrive.Instance.Settings.maxSpeed, pitch, yaw, roll }));
                        else
                            WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { WarpDrive.Instance.Settings.maxSpeed, pitch, yaw, roll });
                    }

                    // Speed -> SendMessageToOthers
                    if (WarpDriveOnGrid != null && currentSpeedPt > 1d)
                    {
                        MyAPIGateway.Multiplayer.SendMessageToOthers(WarpDriveSession.toggleWarpPacketIdSpeed,
                            message: MyAPIGateway.Utilities.SerializeToBinary(new SpeedMessage
                            {
                                EntityId = WarpDriveOnGrid.EntityId,
                                WarpSpeed = currentSpeedPt,
                                Pitch = pitch,
                                Yaw = yaw,
                                Roll = roll
                            }));
                    }

                    // Auto stop if speed to low
                    if (currentSpeedPt <= -1d)
                    {
                        Dewarp();

                        if (WarpDriveOnGrid != null)
                        {
                            foreach (var ActiveDrive in GetActiveWarpDrives())
                            {
                                if (ActiveDrive.Enabled)
                                {
                                    ActiveDrive.Enabled = false;
                                    if (!TempDisabledDrives.Contains(ActiveDrive))
                                        TempDisabledDrives.Add(ActiveDrive);
                                }
                            }
                        }

                        return false;
                    }
                }
            }
            #endregion
            #region Steering_Client only
            else if (!MyAPIGateway.Utilities.IsDedicated && !MyAPIGateway.Multiplayer.IsServer)
            {
                var Hostplayer = MyAPIGateway.Session?.Player;
                var cockpit = Hostplayer?.Character?.Parent;

                // Only helmsman is able to steer the ship
                if (cockpit == controlledCockpit)
                {
                    bool NotPressed_f = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.FORWARD);
                    bool NotPressed_b = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.BACKWARD);
                    bool boost = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.SPRINT);

                    bool NotPressed_up = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROTATION_UP);
                    bool NotPressed_down = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROTATION_DOWN);
                    bool NotPressed_left = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROTATION_LEFT);
                    bool NotPressed_right = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROTATION_RIGHT);
                    bool NotPressed_q = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROLL_LEFT);
                    bool NotPressed_e = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.ROLL_RIGHT);

                    double mouse_x = MyAPIGateway.Input.GetMouseX();
                    double mouse_y = MyAPIGateway.Input.GetMouseY();
                    int mouse_cap = 40;

                    // Fake physics, by hand
                    double totalGyroForce = 0;
                    double inertia = (2d / 5d) * (double)GridsMass.Values.Sum();

                    foreach (MyGyro g in steeringGyros)
                        if (g.IsWorking)
                            totalGyroForce += (double)g.MaxGyroForce;

                    double gyro_inertia = totalGyroForce / inertia;
                    double max_angular_move = ((gyro_inertia * 1d) * Math.Pow(0.016, 2)) / speedDamper(currentSpeedPt);
                    double cap_of_max_angular_move = 2.95d;

                    bool allow_steering = !MyAPIGateway.Gui.IsCursorVisible && !MyAPIGateway.Gui.ChatEntryVisible;

                    // Cap max_angular_move
                    if (max_angular_move > cap_of_max_angular_move)
                        max_angular_move = cap_of_max_angular_move;


                    // update speed
                    if (WarpDriveOnGrid != null && WarpDriveSession.Instance.warpDrivesSpeeds.Count > 0)
                    {
                        double[] movement;
                        double NewSpeed = 0d;
                        if (WarpDriveSession.Instance.warpDrivesSpeeds.TryGetValue(WarpDriveOnGrid, out movement))
                            NewSpeed = movement[0];

                        // if so - there is a movement EVER TIME
                        if (NewSpeed != 0d)
                        {
                            if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                            {
                                if (NewSpeed > WarpDrive.Instance.Settings.AllowInGravityMaxSpeed)
                                {
                                    currentSpeedPt = 1000d / 60d;

                                    if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                        WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] }));
                                    else
                                        WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] });

                                    WarpDriveSession.Instance.TransmitWarpSpeed(WarpDriveOnGrid, currentSpeedPt, movement[1], movement[2], movement[3]);
                                }
                                else
                                {
                                    currentSpeedPt = NewSpeed;

                                    if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                        WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] }));
                                    else
                                        WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] });
                                }
                            }
                            else if (NewSpeed > WarpDrive.Instance.Settings.maxSpeed)
                            {
                                currentSpeedPt = WarpDrive.Instance.Settings.maxSpeed;

                                if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                    WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] }));
                                else
                                    WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] });

                                WarpDriveSession.Instance.TransmitWarpSpeed(WarpDriveOnGrid, WarpDrive.Instance.Settings.maxSpeed, movement[1], movement[2], movement[3]);
                            }
                            else
                            {
                                currentSpeedPt = NewSpeed;

                                if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                    WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] }));
                                else
                                    WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] });
                            }
                        }
                    }

                    // Pitch
                    if (mouse_y != 0 && allow_steering)
                    {
                        if (mouse_y > mouse_cap)
                            mouse_y = mouse_cap;
                        else if (mouse_y < mouse_cap * -1)
                            mouse_y = mouse_cap * -1;

                        pitch = pitch + (max_angular_move / (20d * (mouse_cap / mouse_y)));
                    }
                    else if (!NotPressed_up && NotPressed_down && allow_steering)
                    {
                        // Down
                        if (pitch < 0d)
                            pitch = pitch + (max_angular_move / 20d);
                        else
                            pitch = pitch + (max_angular_move / 70d);
                    }
                    else if (NotPressed_up && !NotPressed_down && allow_steering)
                    {
                        // Up
                        if (pitch > 0d)
                            pitch = pitch - (max_angular_move / 20d);
                        else
                            pitch = pitch - (max_angular_move / 70d);
                    }
                    else
                    {
                        // Inertia - we modify old values to get a better feeling
                        if (pitch >= max_angular_move / 20d)
                            pitch = pitch - (max_angular_move / 20d);
                        else if (pitch <= max_angular_move / 20d * -1)
                            pitch = pitch + (max_angular_move / 20d);
                        else
                            pitch = 0d;
                    }
                    // Cap pitch to max_angular_move
                    if (pitch > cap_of_max_angular_move)
                        pitch = cap_of_max_angular_move;

                    if (pitch < (cap_of_max_angular_move * -1))
                        pitch = (cap_of_max_angular_move * -1);

                    // Yaw
                    if (mouse_x != 0 && allow_steering)
                    {
                        if (mouse_x > mouse_cap)
                            mouse_x = mouse_cap;
                        else if (mouse_x < mouse_cap * -1)
                            mouse_x = mouse_cap * -1;

                        yaw = yaw + (max_angular_move / (20d * (mouse_cap / mouse_x)));
                    }
                    else if (!NotPressed_left && NotPressed_right && allow_steering)
                    {
                        // 
                        if (yaw < 0d)
                            yaw = yaw + (max_angular_move / 20d);
                        else
                            yaw = yaw + (max_angular_move / 70d);
                    }
                    else if (NotPressed_left && !NotPressed_right && allow_steering)
                    {
                        // 
                        if (yaw > 0d)
                            yaw = yaw - (max_angular_move / 20d);
                        else
                            yaw = yaw - (max_angular_move / 70d);
                    }
                    else
                    {
                        // Inertia - we modify old values to get a better feeling
                        if (yaw >= max_angular_move / 20d)
                            yaw = yaw - (max_angular_move / 20d);
                        else if (yaw <= max_angular_move / 20d * -1)
                            yaw = yaw + (max_angular_move / 20d);
                        else
                            yaw = 0d;
                    }
                    // Cap yaw to max_angular_move
                    if (yaw > cap_of_max_angular_move)
                        yaw = cap_of_max_angular_move;

                    if (yaw < (cap_of_max_angular_move * -1))
                        yaw = (cap_of_max_angular_move * -1);

                    // Roll le/ri
                    if (!NotPressed_e && NotPressed_q && allow_steering)
                    {
                        // 
                        if (roll < 0d)
                            roll = roll + (max_angular_move / 20d);
                        else
                            roll = roll + (max_angular_move / 60d); // 70d
                    }
                    else if (NotPressed_e && !NotPressed_q && allow_steering)
                    {
                        // 
                        if (roll > 0d)
                            roll = roll - (max_angular_move / 20d);
                        else
                            roll = roll - (max_angular_move / 60d); // 70d
                    }
                    else
                    {
                        // Inertia - we modify old values to get a better feeling
                        if (roll >= max_angular_move / 20d)
                            roll = roll - (max_angular_move / 20d);
                        else if (roll <= max_angular_move / 20d * -1)
                            roll = roll + (max_angular_move / 20d);
                        else
                            roll = 0d;
                    }
                    // Cap roll to max_angular_move
                    if (roll > cap_of_max_angular_move)
                        roll = cap_of_max_angular_move;

                    if (roll < (cap_of_max_angular_move * -1))
                        roll = (cap_of_max_angular_move * -1);

                    // faster
                    if (!NotPressed_b && NotPressed_f)
                    {
                        if (WarpDriveOnGrid != null)
                        {
                            if (IsPrototech)
                            {
                                // ---- JUST 4 TESTING @ CLIENT
                                //currentSpeedPt += boost ? 30000000.0d : 10000.5d;
                                currentSpeedPt += boost ? 3.0d : 1.5d;
                            }
                            else
                            {
                                currentSpeedPt += boost ? 1.5d : 0.75d;
                            }

                            // set max speed if in gravity
                            if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                            {
                                if (currentSpeedPt > WarpDrive.Instance.Settings.AllowInGravityMaxSpeed)
                                    currentSpeedPt = WarpDrive.Instance.Settings.AllowInGravityMaxSpeed;
                            }
                            else if (currentSpeedPt > WarpDrive.Instance.Settings.maxSpeed)
                                currentSpeedPt = WarpDrive.Instance.Settings.maxSpeed;

                            // set local speed
                            if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, pitch, yaw, roll }));
                            else
                                WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, pitch, yaw, roll });

                            // send speed to server
                            WarpDriveSession.Instance.TransmitWarpSpeed(WarpDriveOnGrid, currentSpeedPt, pitch, yaw, roll);
                        }
                    }

                    //slower
                    if (!NotPressed_f && NotPressed_b)
                    {
                        if (WarpDriveOnGrid != null)
                        {
                            if (IsPrototech)
                            {
                                currentSpeedPt -= boost ? 3.0d : 1.5d;
                            }
                            else
                            {
                                currentSpeedPt -= boost ? 1.5d : 0.75d;
                            }

                            //also minimum speed
                            if (currentSpeedPt < 16d)
                                currentSpeedPt = -5d;

                            //set local speed
                            if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, pitch, yaw, roll }));
                            else
                                WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, pitch, yaw, roll });
                            //send speed
                            WarpDriveSession.Instance.TransmitWarpSpeed(WarpDriveOnGrid, currentSpeedPt, pitch, yaw, roll);
                        }
                    }

                    //neither faster or slower
                    if (!NotPressed_f && !NotPressed_b)
                    {
                        //set local speed
                        if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                            WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, pitch, yaw, roll }));
                        else
                            WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, pitch, yaw, roll });
                        //send speed
                        WarpDriveSession.Instance.TransmitWarpSpeed(WarpDriveOnGrid, currentSpeedPt, pitch, yaw, roll);
                    }

                    // cap to gravity speed
                    if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                    {
                        if (currentSpeedPt > WarpDrive.Instance.Settings.AllowInGravityMaxSpeed)
                        {
                            currentSpeedPt = WarpDrive.Instance.Settings.AllowInGravityMaxSpeed;

                            if (WarpDriveOnGrid != null)
                            {
                                if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                    WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { WarpDrive.Instance.Settings.AllowInGravityMaxSpeed, pitch, yaw, roll }));
                                else
                                    WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { WarpDrive.Instance.Settings.AllowInGravityMaxSpeed, pitch, yaw, roll });
                                WarpDriveSession.Instance.TransmitWarpSpeed(WarpDriveOnGrid, WarpDrive.Instance.Settings.AllowInGravityMaxSpeed, pitch, yaw, roll);
                            }
                        }
                    }

                    // cap to max speed
                    else if (currentSpeedPt > WarpDrive.Instance.Settings.maxSpeed)
                    {
                        currentSpeedPt = WarpDrive.Instance.Settings.maxSpeed;

                        if (WarpDriveOnGrid != null)
                        {
                            if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                                WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { WarpDrive.Instance.Settings.maxSpeed, pitch, yaw, roll }));
                            else
                                WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { WarpDrive.Instance.Settings.maxSpeed, pitch, yaw, roll });

                            WarpDriveSession.Instance.TransmitWarpSpeed(WarpDriveOnGrid, WarpDrive.Instance.Settings.maxSpeed, pitch, yaw, roll);
                        }
                    }

                    // auto stop if speed to low
                    if (currentSpeedPt <= -1d)
                    {
                        WarpDriveSession.Instance.TransmitWarpSpeed(WarpDriveOnGrid, -1d, 0, 0, 0);
                        WarpDriveSession.Instance.TransmitToggleWarp(WarpDriveOnGrid);

                        if (WarpDriveOnGrid != null)
                        {
                            foreach (var ActiveDrive in GetActiveWarpDrives())
                            {
                                if (ActiveDrive.Enabled)
                                {
                                    ActiveDrive.Enabled = false;
                                    if (!TempDisabledDrives.Contains(ActiveDrive))
                                        TempDisabledDrives.Add(ActiveDrive);
                                }
                            }
                        }
                        return false;
                    }
                }
                else
                {
                    // passanger mode, only receive values
                    double[] movement;
                    if (WarpDriveSession.Instance.warpDrivesSpeeds.TryGetValue(WarpDriveOnGrid, out movement))
                    {
                        currentSpeedPt = movement[0];
                        pitch = movement[1];
                        yaw = movement[2];
                        roll = movement[3];
                    }
                    else
                        MyLog.Default.WriteLineAndConsole("FSD: No movement received");
                }
            }
            #endregion
            #region Steering_Server only
            else if (MyAPIGateway.Utilities.IsDedicated)
            {
                if (WarpDriveOnGrid != null && WarpDriveSession.Instance.warpDrivesSpeeds.Count > 0)
                {
                    double[] movement;
                    double NewSpeed = 0d;
                    if (WarpDriveSession.Instance.warpDrivesSpeeds.TryGetValue(WarpDriveOnGrid, out movement))
                        NewSpeed = movement[0];

                    //MyLog.Default.WriteLineAndConsole("X_movement_" + movement[0] + " | " + movement[1] + " | " + movement[2] + " | " + movement[3]);

                    // cap to gravity speed
                    if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                    {
                        if (NewSpeed > WarpDrive.Instance.Settings.AllowInGravityMaxSpeed)
                        {
                            currentSpeedPt = 1000 / 60d;
                            WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] });
                        }
                        else
                            currentSpeedPt = NewSpeed;
                    }

                    // cap to max speed
                    else if (NewSpeed > WarpDrive.Instance.Settings.maxSpeed)
                    {
                        currentSpeedPt = WarpDrive.Instance.Settings.maxSpeed;
                        WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, movement[1], movement[2], movement[3] });
                    }
                    // set correct speed
                    else
                        currentSpeedPt = NewSpeed;

                    // set axis
                    pitch = movement[1];
                    yaw = movement[2];
                    roll = movement[3];

                    /* send speed to others
                    if (WarpDriveOnGrid != null && currentSpeedPt > 1)
                    {
                        MyAPIGateway.Multiplayer.SendMessageToOthers(WarpDriveSession.toggleWarpPacketIdSpeed,
                            message: MyAPIGateway.Utilities.SerializeToBinary(new SpeedMessage
                            {
                                EntityId = WarpDriveOnGrid.EntityId,
                                WarpSpeed = currentSpeedPt,
                                Pitch = pitch,
                                Yaw = yaw,
                                Roll = roll
                            }));
                    }*/
                }

                // auto stop if speed to low
                if (currentSpeedPt <= -1d)
                {
                    Dewarp();
                    return false;
                }
            }
            #endregion

            // go for teleport.
            return true;
        }



        /// <summary>
        /// Higher speeds lower steering
        /// </summary>
        /// <param name="speed">current speed of grid in m/tick</param>
        /// <returns>divisor for inertia</returns>
        private double speedDamper(double speed)
        {
            double speedInKm_s = currentSpeedPt * 60 / 1000;
            // ToDo
            // define a usefull damping for high speeds

            return 1d;
        }
        private float GetRadiusCenter()
        {
            MyCubeGrid sys = grid.MainGrid;
            float s = 0f;
            if (sys.GridSizeEnum == MyCubeSize.Small)
                s = 0f;
            Vector3I v = sys.Max - sys.Min;
            v.Z = 20;
            return ((float)v.Length() / 10) * s;
        }

        // Center 1
        private void DrawAllLinesCenter1()
        {
            if (grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;

            try
            {
                float r = Math.Max(GetRadiusCenter() + 0, 12);
                Vector3D pos = MainGrid.PositionComp.GetPosition();

                var SpeedCorrector = 1200 - (currentSpeedPt / 3);
                Vector3D centerEnd = pos + (gridMatrix.Forward * 240);

                if (MainGrid.GridSizeEnum == MyCubeSize.Small)
                {
                    SpeedCorrector = 600 - (currentSpeedPt / 3);
                    centerEnd = pos + (gridMatrix.Forward * 120);
                }

                Vector3D centerStart = pos - (gridMatrix.Forward * SpeedCorrector);

                // DrawLine(centerStart + (gridMatrix.Left * r), centerEnd + (gridMatrix.Left * r), 15);
                // DrawLine(centerStart + (gridMatrix.Right * r), centerEnd + (gridMatrix.Right * r), 15);
                // DrawLineC(centerStart + (gridMatrix.Up * r), centerEnd + (gridMatrix.Up * r), 15);
                if (MainGrid.GridSizeEnum == MyCubeSize.Small)
                    DrawLineCenter1(centerEnd + (gridMatrix.Down * r), centerStart + (gridMatrix.Down * r), 18);
                else
                    DrawLineCenter1(centerEnd + (gridMatrix.Down * r), centerStart + (gridMatrix.Down * r), 38);
            }
            catch { }
        }

        private void DrawLineCenter1(Vector3D startPos, Vector3D endPos, float rad)
        {
            Vector4 baseCol = Color.SteelBlue;
            string material = "SciFiEngineThrustMiddle"; // IlluminatingShell ReflectorGlareAlphaBlended
            float ranf = MyUtils.GetRandomFloat(1.1f * rad, 1.8f * rad);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.66f);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.33f);
        }

        // Center 2
        private void DrawAllLinesCenter2()
        {
            if (grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;

            try
            {
                float r = Math.Max(GetRadiusCenter() + 0, 12);
                Vector3D pos = MainGrid.PositionComp.GetPosition();
                var SpeedCorrector = 1000 - (currentSpeedPt / 3);
                Vector3D centerEnd = pos + (gridMatrix.Forward * 180);

                if (MainGrid.GridSizeEnum == MyCubeSize.Small)
                {
                    SpeedCorrector = 500 - (currentSpeedPt / 3);
                    centerEnd = pos + (gridMatrix.Forward * 90);
                }

                Vector3D centerStart = pos - (gridMatrix.Forward * SpeedCorrector);
                // DrawLine(centerStart + (gridMatrix.Left * r), centerEnd + (gridMatrix.Left * r), 15);
                // DrawLine(centerStart + (gridMatrix.Right * r), centerEnd + (gridMatrix.Right * r), 15);
                // DrawLineC(centerStart + (gridMatrix.Up * r), centerEnd + (gridMatrix.Up * r), 15);
                if (MainGrid.GridSizeEnum == MyCubeSize.Small)
                    DrawLineCenter2(centerEnd + (gridMatrix.Down * r), centerStart + (gridMatrix.Down * r), 18);
                else
                    DrawLineCenter2(centerEnd + (gridMatrix.Down * r), centerStart + (gridMatrix.Down * r), 38);
            }
            catch { }
        }

        private void DrawLineCenter2(Vector3D startPos, Vector3D endPos, float rad)
        {
            Vector4 baseCol = Color.CornflowerBlue;
            string material = "SciFiEngineThrustMiddle"; // IlluminatingShell ReflectorGlareAlphaBlended
            float ranf = MyUtils.GetRandomFloat(1.1f * rad, 1.8f * rad);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.66f);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.33f);
        }

        // Center 3
        private void DrawAllLinesCenter3()
        {
            if (grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;

            try
            {
                float r = Math.Max(GetRadiusCenter() + 0, 12);
                Vector3D pos = MainGrid.PositionComp.GetPosition();
                var SpeedCorrector = 800 - (currentSpeedPt / 3);
                Vector3D centerEnd = pos + (gridMatrix.Forward * 220);

                if (MainGrid.GridSizeEnum == MyCubeSize.Small)
                {
                    SpeedCorrector = 400 - (currentSpeedPt / 3);
                    centerEnd = pos + (gridMatrix.Forward * 110);
                }

                Vector3D centerStart = pos - (gridMatrix.Forward * SpeedCorrector);
                // DrawLine(centerStart + (gridMatrix.Left * r), centerEnd + (gridMatrix.Left * r), 15);
                // DrawLine(centerStart + (gridMatrix.Right * r), centerEnd + (gridMatrix.Right * r), 15);
                // DrawLineC(centerStart + (gridMatrix.Up * r), centerEnd + (gridMatrix.Up * r), 15);
                if (MainGrid.GridSizeEnum == MyCubeSize.Small)
                    DrawLineCenter3(centerEnd + (gridMatrix.Down * r), centerStart + (gridMatrix.Down * r), 18);
                else
                    DrawLineCenter3(centerEnd + (gridMatrix.Down * r), centerStart + (gridMatrix.Down * r), 38);
            }
            catch { }
        }

        private void DrawLineCenter3(Vector3D startPos, Vector3D endPos, float rad)
        {
            Vector4 baseCol = Color.Indigo;
            string material = "SciFiEngineThrustMiddle"; // IlluminatingShell ReflectorGlareAlphaBlended
            float ranf = MyUtils.GetRandomFloat(1.1f * rad, 1.8f * rad);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.66f);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.33f);
        }

        // Center 4
        private void DrawAllLinesCenter4()
        {
            if (grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;

            try
            {
                float r = Math.Max(GetRadiusCenter() + 0, 12);
                Vector3D pos = MainGrid.PositionComp.GetPosition();
                var SpeedCorrector = 1500 - (currentSpeedPt / 3);
                Vector3D centerEnd = pos + (gridMatrix.Forward * 90);

                if (MainGrid.GridSizeEnum == MyCubeSize.Small)
                {
                    SpeedCorrector = 750 - (currentSpeedPt / 3);
                    centerEnd = pos + (gridMatrix.Forward * 45);
                }

                Vector3D centerStart = pos - (gridMatrix.Forward * SpeedCorrector);
                // DrawLine(centerStart + (gridMatrix.Left * r), centerEnd + (gridMatrix.Left * r), 15);
                // DrawLine(centerStart + (gridMatrix.Right * r), centerEnd + (gridMatrix.Right * r), 15);
                // DrawLineC(centerStart + (gridMatrix.Up * r), centerEnd + (gridMatrix.Up * r), 15);

                if (MainGrid.GridSizeEnum == MyCubeSize.Small)
                    DrawLineCenter4(centerEnd + (gridMatrix.Down * r), centerStart + (gridMatrix.Down * r), 18);
                else
                    DrawLineCenter4(centerEnd + (gridMatrix.Down * r), centerStart + (gridMatrix.Down * r), 38);
            }
            catch { }
        }

        private void DrawLineCenter4(Vector3D startPos, Vector3D endPos, float rad)
        {
            Vector4 baseCol = Color.LightGoldenrodYellow;
            string material = "SciFiEngineThrustMiddle"; // IlluminatingShell ReflectorGlareAlphaBlended
            float ranf = MyUtils.GetRandomFloat(1.1f * rad, 1.8f * rad);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.66f);
            MySimpleObjectDraw.DrawLine(startPos, endPos, MyStringId.GetOrCompute(material), ref baseCol, ranf * 0.33f);
        }

        /*
        private void StartBlinkParticleEffect()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            if (grid.MainGrid == null)
                return;

            try
            {
                BlinkTrailEffect?.Stop();

                var Grid = grid.MainGrid as IMyCubeGrid;
                Vector3D direction = gridMatrix.Forward;

                float gridDepthOffset = 0.09f * Grid.LocalAABB.Depth;

                if (Grid.LocalAABB.Depth < 45 && grid.MainGrid.GridSizeEnum == MyCubeSize.Large)
                    gridDepthOffset = 0.3f * Grid.LocalAABB.Depth;
                else if (Grid.LocalAABB.Depth > 120 && grid.MainGrid.GridSizeEnum == MyCubeSize.Large)
                    gridDepthOffset = 0.05f * Grid.LocalAABB.Depth;

                float gridWidth = Grid.LocalAABB.Width > Grid.LocalAABB.Height ? Grid.LocalAABB.Width : Grid.LocalAABB.Height;
                float scale = gridWidth * 2;
                float particleHalfLength = 2.565f;

                MatrixD rotationMatrix = MatrixD.CreateFromYawPitchRoll(MathHelper.ToRadians(0), MathHelper.ToRadians(-90), MathHelper.ToRadians(0));
                rotationMatrix.Translation = new Vector3D(0, 0, (particleHalfLength * scale) + gridDepthOffset + Grid.GridSize);

                Vector3D effectOffset = direction * Grid.WorldAABB.HalfExtents.AbsMax();
                Vector3D origin = Grid.WorldAABB.Center;

                MatrixD fromDir = MatrixD.CreateFromDir(direction);
                fromDir.Translation = origin - effectOffset;

                fromDir = rotationMatrix * fromDir;

                MyParticlesManager.TryCreateParticleEffect("BlinkDriveTrail", ref fromDir, ref origin, uint.MaxValue, out BlinkTrailEffect);

                BlinkTrailEffect.UserScale = scale;

                if (Grid.Physics != null)
                    BlinkTrailEffect.Velocity = Grid.Physics.LinearVelocity;
            }
            catch (Exception e)
            {
                MyLog.Default.Error(e.ToString());
            }
        }
        */

        public void StopBlinkParticleEffect()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
                BlinkTrailEffect?.Stop();
        }

        private bool FindPlayerInCockpit()
        {
            if (grid.MainGrid == null)
                return false;

            HashSet<IMyShipController> gridCockpits;
            if (grid.cockpits.TryGetValue(grid.MainGrid, out gridCockpits))
            {
                if (gridCockpits.Count > 0)
                {
                    foreach (IMyShipController cockpit in gridCockpits)
                    {
                        if (cockpit != null && cockpit.IsUnderControl)
                            return true;
                    }
                }
            }

            return false;
        }

        public void ToggleWarp(IMyTerminalBlock block, IMyCubeGrid source, long PlayerID)
        {
            WarpDrive drive = block?.GameLogic?.GetAs<WarpDrive>();
            if (drive != null)
            {
                if (drive.System.WarpState == State.Idle)
                {
                    if (!hasEnoughPower || !FindPlayerInCockpit())
                        return;

                    if (MyAPIGateway.Utilities.IsDedicated || MyAPIGateway.Multiplayer.IsServer)
                    {
                        WarpDriveSession.Instance.RefreshGridCockpits(block);
                        MatrixD gridMatrix = drive.System.grid.FindWorldMatrix();

                        string msg = WarpDrive.Instance.ProxymityDangerCharge(gridMatrix, source);
                        if (msg != null)
                        {
                            SendMessage(ProximityChargeAlert + msg, 2f, "Red", PlayerID);
                            WarpState = State.Idle;
                            return;
                        }

                        MyAPIGateway.Multiplayer.SendMessageToOthers(WarpDriveSession.toggleWarpPacketId,
                            message: MyAPIGateway.Utilities.SerializeToBinary(new ItemsMessage
                            {
                                EntityId = block.EntityId,
                                SendingPlayerID = PlayerID
                            }));
                    }

                    StartCharging(PlayerID);
                    startWarpSource = source;

                    if (!MyAPIGateway.Utilities.IsDedicated && !MyAPIGateway.Multiplayer.IsServer)
                        WarpDriveSession.Instance.TransmitWarpConfig(Settings.Instance, block.EntityId);
                }
                else
                {
                    drive.System.Dewarp();

                    var MyGrid = drive.Block.CubeGrid as MyCubeGrid;
                    if (GetActiveWarpDrive(MyGrid) != null)
                    {
                        foreach (var ActiveDrive in GetActiveWarpDrives())
                        {
                            if (ActiveDrive.Enabled)
                            {
                                ActiveDrive.Enabled = false;
                                if (!TempDisabledDrives.Contains(ActiveDrive))
                                    TempDisabledDrives.Add(ActiveDrive);
                            }
                        }
                    }
                }
            }
        }

        public bool Contains(WarpDrive drive)
        {
            return grid.Contains((MyCubeGrid)drive.Block.CubeGrid);
        }

        private List<long> FindAllPlayersInGrid(GridSystem System)
        {
            var PlayersIdList = new List<long>();

            if (System != null)
            {
                foreach (var grid in System.Grids)
                {
                    foreach (var Block in grid.GetFatBlocks())
                    {
                        if (Block == null)
                            continue;

                        var Cockpit = Block as IMyCockpit;
                        var CryoChamber = Block as IMyCryoChamber;

                        if (Cockpit != null)
                        {
                            if (Cockpit.Pilot != null)
                            {
                                PlayersIdList.Add(Cockpit.Pilot.EntityId);
                                continue;
                            }
                        }

                        if (CryoChamber != null)
                        {
                            if (CryoChamber.Pilot != null)
                                PlayersIdList.Add(CryoChamber.Pilot.EntityId);
                        }
                    }
                }
            }
            return PlayersIdList;
        }

        private bool ConnectedStatic(IMyCubeGrid MyGrid)
        {
            if (MyGrid == null)
                return false;

            var AttachedList = new List<IMyCubeGrid>();
            MyAPIGateway.GridGroups.GetGroup(MyGrid, GridLinkTypeEnum.Physical, AttachedList);

            if (AttachedList.Count > 1)
            {
                foreach (var AttachedGrid in AttachedList)
                {
                    if (AttachedGrid != null)
                    {
                        if (AttachedGrid.IsStatic)
                            return true;
                    }
                }
            }
            return false;
        }

        private void StartCharging(long PlayerID)
        {
            if (grid.MainGrid == null)
                return;

            if (IsInGravity())
            {
                SendMessage(warnNoEstablish, 5f, "Red", PlayerID);
                WarpState = State.Idle;
                return;
            }

            if (ConnectedStatic(grid.MainGrid))
            {
                SendMessage(warnStatic, 5f, "Red", PlayerID);
                WarpState = State.Idle;
                return;
            }

            if (!grid.IsStatic)
            {
                WarpState = State.Charging;
                startChargeRuntime = WarpDriveSession.Instance.Runtime;

                if (MyAPIGateway.Utilities.IsDedicated)
                {
                    if (PlayerID > 0)
                    {
                        foreach (var Player in OnlinePlayersList)
                        {
                            if (Player.IdentityId == PlayerID)
                            {
                                if (!PlayersInWarpList.Contains(Player))
                                    PlayersInWarpList.Add(Player);
                            }
                        }
                    }
                }

                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    if (IsPrototech)
                    {
                        sound.PlaySound(WarpConstants.PrototechChargingSound, true);
                        sound.VolumeMultiplier = 1;
                    }
                    else
                    {
                        sound.PlaySound(WarpConstants.chargingSound, true);
                        sound.VolumeMultiplier = 2;
                    }
                    PlayParticleEffect();
                }
            }
            else
                SendMessage(warnStatic, 5f, "Red", PlayerID);
        }

        private void StartWarp()
        {
            if (grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;

            if (IsInGravity())
            {
                SendMessage(warnNoEstablish);
                Dewarp();
                return;
            }

            if (grid.IsStatic)
            {
                SendMessage(warnStatic);
                Dewarp();
                return;
            }

            if (ConnectedStatic(MainGrid))
            {
                SendMessage(warnStatic);
                Dewarp();
                return;
            }

            // Get all attached grids incl. magnetic locks, connectors, pistons .... 
            MyAPIGateway.GridGroups.GetGroup(MainGrid, GridLinkTypeEnum.Physical, allAttachedGrids);
            List<IMyShipConnector> myShipConnectors = new List<IMyShipConnector>();
            List<IMyLandingGear> myLandingGears = new List<IMyLandingGear>();

            foreach (MyCubeGrid myGrid in allAttachedGrids)
            {
                foreach (IMySlimBlock slim in myGrid.CubeBlocks)
                {
                    // Sorted by occurance chance, atm no performance problems
                    if (slim.FatBlock == null)
                        continue;

                    // In order to steer in FSD, entire gyroscopes work together for that group of grids
                    else if (slim.FatBlock is MyGyro)
                        steeringGyros.Add((MyGyro)slim.FatBlock);

                    else if (slim.FatBlock is IMyLandingGear)
                        myLandingGears.Add((IMyLandingGear)slim.FatBlock);

                    // Need to check if yellow
                    else if (slim.FatBlock is IMyShipConnector)
                        myShipConnectors.Add((IMyShipConnector)slim.FatBlock);

                    // Need to attach after rePhysic, if we know them its faster

                    else if (slim.FatBlock is IMyMotorAdvancedStator)
                    {
                        //slimStators.Add(slim);
                        IMyMechanicalConnectionBlock block = (IMyMechanicalConnectionBlock)slim.FatBlock;
                        mechConnections.Add(new MechItem(block, block.Top));
                    }
                    // Lets try to combine both to a IMyMechanicalConnectionBlock
                    else if (slim.FatBlock is IMyMotorStator)
                    {
                        //slimStators.Add(slim);
                        IMyMechanicalConnectionBlock block = (IMyMechanicalConnectionBlock)slim.FatBlock;
                        mechConnections.Add(new MechItem(block, block.Top));
                    }

                    // For faster accessing physics
                    else if (slim.FatBlock is MyAdvancedDoor)
                        foreach (MyEntitySubpart part in ((MyAdvancedDoor)slim.FatBlock).Subparts.Values)
                            physicParts.Add(part);
                    else
                    {
                        // nothing ToDo here
                    }
                }
            }

            foreach (IMyShipConnector myShipConnector in myShipConnectors)
            {
                // If we allow start with grids' connectors at YELLOW server will crash
                if (myShipConnector.Status == Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connectable)
                {
                    SendMessage(warnConnectorYellow);

                    clear_all_Lists();
                    Dewarp();
                    return;
                }
            }

            foreach (IMyLandingGear myLandingGear in myLandingGears)
            {
                // If we allow FSD with grids' LandingGears at YELLOW grid is unable to move after FSD
                if (myLandingGear.LockMode == SpaceEngineers.Game.ModAPI.Ingame.LandingGearMode.ReadyToLock)
                {
                    SendMessage(warnConnectorYellow);

                    clear_all_Lists();
                    Dewarp();
                    return;
                }
            }

            // No more checks -> FSD ENGAGE
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                if (effect != null)
                    StopParticleEffect();

                if (IsPrototech)
                {
                    sound.PlaySound(WarpConstants.PrototechJumpInSound, true);
                    sound.VolumeMultiplier = 1;
                }
                else
                {
                    sound.PlaySound(WarpConstants.jumpInSound, true);
                    sound.VolumeMultiplier = 1;
                }
            }

            WarpState = State.Active;

            // Get needed variables before unPhysic
            gridRadius = ((IMyCubeGrid)MainGrid).WorldVolume.Radius;

            Vector3D? currentVelocity = MainGrid?.Physics?.LinearVelocity;
            if (currentVelocity.HasValue)
            {
                gridMatrix = grid.FindWorldMatrix();

                /* // people asked to get the start speed no matter what was the ship normal speed before warp.
                double dot = Vector3D.Dot(currentVelocity.Value, gridMatrix.Forward);
                if (double.IsNaN(dot) || gridMatrix == MatrixD.Zero)
                    dot = 0;

                currentSpeedPt = MathHelper.Clamp(dot, WarpDrive.Instance.Settings.startSpeed, WarpDrive.Instance.Settings.maxSpeed);
                */

                if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                {
                    currentSpeedPt = 1000 / 60d;
                }
                else
                    currentSpeedPt = WarpDrive.Instance.Settings.startSpeed;

                var WarpDriveOnGrid = GetActiveWarpDrive(MainGrid);
                if (WarpDriveOnGrid != null)
                {
                    if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                        WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, pitch, yaw, roll }));
                    else
                        WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, pitch, yaw, roll });
                }
            }
            else
            {
                if (WarpDrive.Instance.Settings.AllowInGravity && GridGravityNow() > 0)
                {
                    currentSpeedPt = 1000 / 60d;
                }
                else
                    currentSpeedPt = WarpDrive.Instance.Settings.startSpeed;

                var WarpDriveOnGrid = GetActiveWarpDrive(MainGrid);
                if (WarpDriveOnGrid != null)
                {
                    if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                        WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, pitch, yaw, roll }));
                    else
                        WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, pitch, yaw, roll });
                }
            }

            #region +++StartWarp
            // Physic magic 
            /* Do not:
             * Detach rotors here
             */

            physicParts.Reverse();
            foreach (MyEntitySubpart part in physicParts)
            {
                part.Physics.Enabled = false;
                part.StopPhysicsActivation = true;
            }

            foreach (MyCubeGrid myGrid in allAttachedGrids)
            {
                if (myGrid == MainGrid)
                    continue;

                // We need Physics.Clear() to get correct data
                myGrid.Physics.Enabled = false;
                myGrid.StopPhysicsActivation = true;
            }

            MainGrid.Physics.Enabled = false;

            // We need a Teleport fix if we want physic back
            // regardless what happens after this
            fixPhysics = true;
            #endregion

            var PlayersIdsOnGrid = FindAllPlayersInGrid(grid);

            if (PlayersIdsOnGrid != null && PlayersIdsOnGrid.Count > 0)
            {
                foreach (var OnlinePlayer in OnlinePlayersList)
                {
                    if (OnlinePlayer.Character != null && PlayersIdsOnGrid.Contains(OnlinePlayer.Character.EntityId) && !PlayersInWarpList.Contains(OnlinePlayer))
                        PlayersInWarpList.Add(OnlinePlayer);
                }
            }
        }

        private IMyFunctionalBlock GetActiveWarpDrive(MyCubeGrid MyGrid)
        {
            HashSet<WarpDrive> controllingDrives;
            if (startWarpSource == null || !warpDrives.TryGetValue(startWarpSource, out controllingDrives))
            {
                if (MyGrid == null || !warpDrives.TryGetValue(MyGrid, out controllingDrives))
                    controllingDrives = warpDrives.FirstPair().Value;
            }

            if (controllingDrives == null)
                return null;

            foreach (WarpDrive drive in controllingDrives)
            {
                if (drive.Block.IsFunctional && drive.Block.IsWorking)
                    return drive.Block;
            }
            return null;
        }

        private List<IMyFunctionalBlock> GetActiveWarpDrives()
        {
            HashSet<WarpDrive> controllingDrives;
            var GridDrives = new List<IMyFunctionalBlock>();
            if (startWarpSource == null || !warpDrives.TryGetValue(startWarpSource, out controllingDrives))
            {
                if (grid.MainGrid == null || !warpDrives.TryGetValue(grid.MainGrid, out controllingDrives))
                    controllingDrives = warpDrives.FirstPair().Value;
            }

            if (controllingDrives == null)
                controllingDrives = new HashSet<WarpDrive>();

            foreach (WarpDrive drive in controllingDrives)
            {
                if (drive.Block.IsFunctional && drive.Block.IsWorking)
                    GridDrives.Add(drive.Block);
            }
            return GridDrives;
        }

        public void Dewarp(bool Collision = false)
        {
            if (WarpState == State.Active && grid?.MainGrid != null && currentSpeedPt > 0 && timeInWarpCounter >= 3600)
            {
                Vector3D exitPosition = grid.MainGrid.PositionComp.GetPosition();
                float totalPowerUsage = CalculateTotalPowerUsage();

                if (MyAPIGateway.Multiplayer.IsServer || MyAPIGateway.Utilities.IsDedicated)
                {
                    string gpsName = $"Slipspace™ Exit Signature ({Math.Floor(totalPowerUsage)}MW)";
                    MyVisualScriptLogicProvider.AddGPSForAll(gpsName, "A ship has exited slipspace™ here!", exitPosition, Color.White, 30);
                }
            }

            if (PlayersInWarpList.Count > 0)
            {
                foreach (var Player in PlayersInWarpList)
                {
                    if (Player == null || Player.Character == null)
                        continue;

                    if (!Player.Character.Save)
                        Player.Character.Save = true;
                }
            }

            #region+++lastJump
            // lastjump
            // 0 = no trigger
            // 1 = stop ship (collision)
            // 2 = regular, keep starting speed
            if (gotTeleported)
                lastJump = Collision ? 1 : 2;
            #endregion

            TeleportNow = false;

            if (grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;
            var WarpDriveOnGrid = GetActiveWarpDrive(MainGrid);

            if (WarpDriveOnGrid != null && WarpState == State.Active && (MyAPIGateway.Multiplayer.IsServer || MyAPIGateway.Utilities.IsDedicated))
            {
                if (WarpDriveOnGrid != null)
                {
                    MyAPIGateway.Multiplayer.SendMessageToOthers(WarpDriveSession.toggleWarpPacketId,
                    message: MyAPIGateway.Utilities.SerializeToBinary(new ItemsMessage
                    {
                        EntityId = WarpDriveOnGrid.EntityId,
                        SendingPlayerID = 0
                    }));
                }
            }


            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                StopParticleEffect();
                StopBlinkParticleEffect();

                sound.SetPosition(MainGrid.PositionComp.GetPosition());
                sound?.StopSound(false);

                if (WarpState == State.Active)
                {
                    if (ProxymityStop)
                    {
                        if (IsPrototech)
                        {
                            sound.PlaySound(WarpConstants.PrototechJumpOutSound, true);
                            sound.VolumeMultiplier = 1;
                        }
                        else
                        {
                            sound.PlaySound(WarpConstants.jumpOutSound, true);
                            sound.VolumeMultiplier = 1;
                        }
                        ProxymityStop = false;
                    }
                    else
                    {
                        if (currentSpeedPt < -1)
                        {
                            if (IsPrototech)
                            {
                                sound.PlaySound(WarpConstants.PrototechJumpOutSound, true);
                                sound.VolumeMultiplier = 1;
                            }
                            else
                            {
                                sound.PlaySound(WarpConstants.jumpOutSound, true);
                                sound.VolumeMultiplier = 1;
                            }
                        }

                        if (functionalDrives == 0)
                        {
                            sound.PlaySound(WarpConstants.EmergencyDropSound, true);
                            sound.VolumeMultiplier = 1;
                        }

                        if (!hasEnoughPower)
                        {
                            sound.PlaySound(WarpConstants.EmergencyDropSound, true);
                            sound.VolumeMultiplier = 1;
                        }

                        if (IsInGravity())
                        {
                            sound.PlaySound(WarpConstants.EmergencyDropSound, true);
                            sound.VolumeMultiplier = 1;
                        }

                        if (IsPrototech)
                        {
                            sound.PlaySound(WarpConstants.PrototechJumpOutSound, true);
                            sound.VolumeMultiplier = 1;
                        }
                        else
                        {
                            sound.PlaySound(WarpConstants.jumpOutSound, true);
                            sound.VolumeMultiplier = 1;
                        }
                    }
                }
            }

            WarpState = State.Idle;

            currentSpeedPt = WarpDrive.Instance.Settings.startSpeed;

            if (PlayersInWarpList.Count > 0)
                PlayersInWarpList.Clear();

            if (WarpDriveOnGrid != null)
            {
                if (WarpDriveSession.Instance == null)
                    return;

                if (!WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(WarpDriveOnGrid))
                    WarpDriveSession.Instance.warpDrivesSpeeds.Add(WarpDriveOnGrid, (new double[] { currentSpeedPt, pitch, yaw, roll }));
                else
                    WarpDriveSession.Instance.warpDrivesSpeeds[WarpDriveOnGrid] = (new double[] { currentSpeedPt, pitch, yaw, roll });
            }
        }

        private float CalculateTotalPowerUsage()
        {
            float totalPower = 0;
            HashSet<WarpDrive> controllingDrives = new HashSet<WarpDrive>();
            if (startWarpSource == null || !warpDrives.TryGetValue(startWarpSource, out controllingDrives))
            {
                if (grid.MainGrid == null || !warpDrives.TryGetValue(grid.MainGrid, out controllingDrives))
                    controllingDrives = warpDrives.FirstPair().Value;
            }

            foreach (WarpDrive drive in controllingDrives)
            {
                if (drive == null || drive.Block == null || drive.Block.CubeGrid == null)
                    continue;

                if (drive.Block.IsFunctional && drive.Block.IsWorking)
                {
                    totalPower += drive.RequiredPower;
                }
            }

            return totalPower;
        }

        private void InCharge()
        {
            if (grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;

            if (functionalDrives == 0)
            {
                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    sound.PlaySound(WarpConstants.EmergencyDropSound, true);
                    sound.VolumeMultiplier = 1;
                }
                SendMessage(warnDamaged);
                Dewarp();
                return;
            }

            if (!hasEnoughPower)
            {
                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    sound.PlaySound(WarpConstants.EmergencyDropSound, true);
                    sound.VolumeMultiplier = 1;
                }
                SendMessage(warnNoPower);
                Dewarp();
                return;
            }

            if (IsInGravity())
            {
                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    sound.PlaySound(WarpConstants.EmergencyDropSound, true);
                    sound.VolumeMultiplier = 1;
                }
                SendMessage(warnNoEstablish);
                Dewarp();
                return;
            }

            if (grid.IsStatic)
            {
                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    sound.PlaySound(WarpConstants.EmergencyDropSound, true);
                    sound.VolumeMultiplier = 1;
                }
                SendMessage(warnStatic);
                Dewarp();
                return;
            }

            if (ConnectedStatic(MainGrid))
            {
                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    sound.PlaySound(WarpConstants.EmergencyDropSound, true);
                    sound.VolumeMultiplier = 1;
                }
                SendMessage(warnStatic);
                Dewarp();
                return;
            }

            // just a test maybe it works >> could be load heavy
            string s2 = WarpDrive.Instance.ProxymityDangerCharge(MainGrid.WorldMatrix, MainGrid);
            if (s2 != null)
            {
                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    sound.PlaySound(WarpConstants.EmergencyDropSound, true);
                    sound.VolumeMultiplier = 1;
                }
                SendMessage(ProximityChargeAlert + s2);
                Dewarp();
                return;
            }

            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                if (effect != null)
                    effect.WorldMatrix = MatrixD.CreateWorld(effect.WorldMatrix.Translation, -gridMatrix.Forward, gridMatrix.Up);

                UpdateParticleEffect();
            }

            if (WarpDrive.Instance.Settings.AllowToDetectEnemyGrids && WarpDrive.Instance.EnemyProxymityDangerCharge(MainGrid))
            {
                var DelayTime = WarpDrive.Instance.Settings.DelayJumpIfEnemyIsNear * 60;
                var ElapsedTime = Math.Abs(WarpDriveSession.Instance.Runtime - startChargeRuntime);
                var ElapsedTimeDevided = ElapsedTime / 60;

                if (ElapsedTime >= DelayTime)
                {
                    if (MainGrid != null && MainGrid.Physics != null)
                    {
                        // store ship speed before WARP. so we can restore it when exit warp.
                        GridSpeedLinearVelocity[MainGrid.EntityId] = MainGrid.Physics.LinearVelocity;
                        GridSpeedAngularVelocity[MainGrid.EntityId] = MainGrid.Physics.AngularVelocity;
                    }

                    StartWarp();
                }
                else if (ElapsedTimeDevided == 11 || ElapsedTimeDevided == 21 || ElapsedTimeDevided == 31 || ElapsedTimeDevided == 41 || ElapsedTimeDevided == 51)
                {
                    if (!MyAPIGateway.Utilities.IsDedicated)
                    {
                        StopParticleEffectNow();
                        PlayParticleEffect();
                    }
                }
            }
            else
            {
                var JumpTimeLogic = IsPrototech ? WarpDrive.Instance.Settings.PrototechJump : WarpDrive.Instance.Settings.DelayJump;
                if (Math.Abs(WarpDriveSession.Instance.Runtime - startChargeRuntime) >= JumpTimeLogic * 60)
                {
                    if (MainGrid.Physics != null)
                    {
                        // store ship speed before WARP. so we can restore it when exit warp.
                        GridSpeedLinearVelocity[MainGrid.EntityId] = MainGrid.Physics.LinearVelocity;
                        GridSpeedAngularVelocity[MainGrid.EntityId] = MainGrid.Physics.AngularVelocity;
                    }

                    StartWarp();
                }
            }
        }

        bool IsInGravity()
        {
            // Better Performance Possible
            if (grid == null || grid.MainGrid == null)
                return true;

            var MainGrid = grid.MainGrid;
            var gravityVectorTemp = 0.0f;
            Vector3D position = MainGrid.PositionComp.GetPosition();
            var gravityVector = MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out gravityVectorTemp);
            var GridGravityCalc = gravityVector.Length() / EARTH_GRAVITY;

            if (WarpDrive.Instance.Settings.AllowInGravity)
            {
                if (GridGravityCalc > WarpDrive.Instance.Settings.AllowInGravityMax)
                    return true;

                if (GridGravityCalc > 0)
                {
                    var worldAABB = MainGrid.PositionComp.WorldAABB;
                    var closestPlanet = MyGamePruningStructure.GetClosestPlanet(ref worldAABB);

                    if (closestPlanet != null && MainGrid.Physics != null)
                    {
                        var mainGridPosition = MainGrid.PositionComp.GetPosition();
                        var closestSurfacePointGlobal = closestPlanet.GetClosestSurfacePointGlobal(ref mainGridPosition);
                        var elevation = double.PositiveInfinity;

                        elevation = Vector3D.Distance(closestSurfacePointGlobal, mainGridPosition);
                        return elevation < WarpDrive.Instance.Settings.AllowInGravityMinAltitude && elevation != double.PositiveInfinity;
                    }
                    else
                        return false;
                }
                else
                    return false;
            }

            return GridGravityCalc > 0.01;
        }

        double getElevation()
        {
            // Better Performance Possible
            double elevation = double.PositiveInfinity;
            if (grid == null || grid.MainGrid == null)
                return elevation;

            var MainGrid = grid.MainGrid;
            var mainGridPosition = MainGrid.PositionComp.GetPosition();

            // Iterate through all entities in the game,
            // Check if the entity is a planet
            // Calculate distance's
            // take nearest
            foreach (var entity in MyEntities.GetEntities())
                if (entity is MyPlanet)
                {
                    double e = 0f;
                    var closestSurfacePointGlobal = ((MyPlanet)entity).GetClosestSurfacePointGlobal(ref mainGridPosition);
                    e = Vector3D.Distance(closestSurfacePointGlobal, mainGridPosition);

                    if (e < elevation)
                        elevation = e;
                }

            return elevation;
        }

        float GridGravityNow()
        {
            if (grid == null || grid.MainGrid == null)
                return 0;

            var gravityVectorTemp = 0.0f;
            Vector3D position = grid.MainGrid.PositionComp.GetPosition();
            var gravityVector = MyAPIGateway.Physics.CalculateNaturalGravityAt(position, out gravityVectorTemp);
            var GridGravityCalc = gravityVector.Length() / EARTH_GRAVITY;

            return GridGravityCalc;
        }

        private void UpdateHeatPower()
        {
            float totalPower = 0;
            int numFunctional = 0;
            hasEnoughPower = true;

            try
            {
                if (warpDrives == null || warpDrives.Count == 0)
                    return;

                HashSet<WarpDrive> controllingDrives = new HashSet<WarpDrive>();
                if (startWarpSource == null || !warpDrives.TryGetValue(startWarpSource, out controllingDrives))
                {
                    if (grid.MainGrid == null || !warpDrives.TryGetValue(grid.MainGrid, out controllingDrives))
                        controllingDrives = warpDrives.FirstPair().Value;
                }

                if (WarpState == State.Charging)
                {
                    if (controllingDrives == null)
                        controllingDrives = new HashSet<WarpDrive>();

                    foreach (WarpDrive drive in controllingDrives)
                    {
                        if (drive == null || drive.Block == null || drive.Block.CubeGrid == null)
                            continue;

                        float _mass = 0f;

                        if (!GridsMass.ContainsKey(drive.Block.CubeGrid.EntityId))
                        {
                            _mass = CulcucateGridGlobalMass(drive.Block.CubeGrid);
                            GridsMass.Add(drive.Block.CubeGrid.EntityId, _mass);
                        }
                        else
                            _mass = GridsMass[drive.Block.CubeGrid.EntityId];

                        if (MassChargeUpdate >= 60)
                        {
                            MassChargeUpdate = 0;
                            _mass = CulcucateGridGlobalMass(drive.Block.CubeGrid);
                            GridsMass[drive.Block.CubeGrid.EntityId] = _mass;
                        }
                        else
                            MassChargeUpdate++;

                        if (_mass == 0)
                        {
                            if (drive.Block.CubeGrid.GridSizeEnum == MyCubeSize.Small)
                                _mass = 150000f;
                            else
                                _mass = 500000f;
                        }

                        // CHARGING Power - !!!! We could add ANY other blocks here, but need to be add to WarpDrive.cs too !!!!
                        switch (drive.Block.BlockDefinition.SubtypeId)
                        {
                            // Updates like intels tik-tok process
                            // Vanilla >> regular power and size
                            case "SlipspaceCoreSmall":
                                // powerMultiplier = 1;
                                totalPower = WarpDrive.Instance.Settings.baseRequiredPowerSmall + (_mass * 2.1f / 100000f);
                                break;

                            case "SlipspaceCoreLarge":
                                // powerMultiplier = 1;
                                totalPower = WarpDrive.Instance.Settings.baseRequiredPower + (_mass * 2.1f / 100000f);
                                break;

                            case "PrototechSlipspaceCoreSmall":
                                // powerMultiplier = 0.5;
                                totalPower = 0.5f * (WarpDrive.Instance.Settings.baseRequiredPowerSmall + (_mass * 2.1f / 100000f));
                                break;

                            case "PrototechSlipspaceCoreLarge":
                                // powerMultiplier = 0.5;
                                totalPower = 0.5f * (WarpDrive.Instance.Settings.baseRequiredPower + (_mass * 2.1f / 100000f)); // or is 1000000f correct?
                                break;

                            default:
                                // No drive found - deactivated
                                break;
                        }
                    }
                }

                if (WarpState == State.Active && grid.MainGrid != null)
                {
                    float _mass;
                    var MainGrid = grid.MainGrid;

                    if (GridsMass.ContainsKey(MainGrid.EntityId))
                    {
                        if (MassUpdateTick++ >= 1200)
                        {
                            MassUpdateTick = 0;
                            _mass = CulcucateGridGlobalMass(MainGrid);
                            GridsMass[MainGrid.EntityId] = _mass;
                        }
                        else
                            _mass = GridsMass[MainGrid.EntityId];
                    }
                    else
                    {
                        _mass = CulcucateGridGlobalMass(MainGrid);
                        GridsMass.Add(MainGrid.EntityId, _mass);
                    }

                    float SpeedNormalize = (float)(currentSpeedPt * 0.06); // 60 / 1000
                    float SpeedCalc = 1f + (SpeedNormalize * SpeedNormalize);

                    float MassCalc;
                    if (MainGrid.GridSizeEnum == MyCubeSize.Small)
                        MassCalc = _mass * (SpeedCalc / 0.528f) / 700000f;
                    else
                        MassCalc = _mass * (SpeedCalc / 0.528f) / 1000000f;

                    float percent = (float)(1f + currentSpeedPt / WarpDrive.Instance.Settings.maxSpeed * WarpDrive.Instance.Settings.powerRequirementMultiplier) + MassCalc;

                    if (percent == 0)
                        percent = 1;

                    foreach (WarpDrive drive in controllingDrives)
                    {
                        if (drive == null || drive.Block == null || drive.Block.CubeGrid == null)
                            continue;

                        if (drive.Block.IsFunctional && drive.Block.IsWorking)
                        {
                            // ACTIVE Power - !!!! We could add ANY other blocks here, but need to be add to WarpDrive.cs too !!!!
                            switch (drive.Block.BlockDefinition.SubtypeId)
                            {
                                // Updates like intels tik-tok process
                                // Vanilla >> regular power and size
                                case "SlipspaceCoreSmall":
                                    // powerMultiplier = 1;
                                    totalPower = (WarpDrive.Instance.Settings.baseRequiredPowerSmall + percent) / WarpDrive.Instance.Settings.powerRequirementBySpeedDeviderSmall;
                                    break;

                                case "SlipspaceCoreLarge":
                                    // powerMultiplier = 1;
                                    totalPower = (WarpDrive.Instance.Settings.baseRequiredPower + percent) / WarpDrive.Instance.Settings.powerRequirementBySpeedDeviderLarge;
                                    break;

                                // Tech2x smaller, reduced power needed (85%)
                                case "PrototechSlipspaceCoreSmall":
                                    // powerMultiplier = 0.85;
                                    totalPower = 0.5f * (WarpDrive.Instance.Settings.baseRequiredPowerSmall + percent) / WarpDrive.Instance.Settings.powerRequirementBySpeedDeviderSmall;
                                    break;

                                case "PrototechSlipspaceCoreLarge":
                                    // powerMultiplier = 0.85;
                                    totalPower = 0.5f * (WarpDrive.Instance.Settings.baseRequiredPower + percent) / WarpDrive.Instance.Settings.powerRequirementBySpeedDeviderLarge;
                                    break;
                                default:
                                    // No drive found - deactivated
                                    break;
                            }
                        }
                    }
                }

                foreach (WarpDrive drive in controllingDrives)
                {
                    if (drive == null || drive.Block == null)
                        continue;

                    if (drive.Block.IsFunctional && drive.Block.IsWorking)
                    {
                        numFunctional++;

                        if (functionalDrives == 0)
                        {
                            // First tick
                            drive.RequiredPower = totalPower / controllingDrives.Count;
                        }
                        else
                        {
                            if (WarpState != State.Idle)
                            {
                                // give SIM some chance before drop warp if power check missed.
                                if (PowerCheckTick++ > 20)
                                {
                                    PowerCheckTick = 0;
                                    double LocalcurrentSpeedPt = currentSpeedPt;

                                    if (!drive.HasPower)
                                    {
                                        if (LocalcurrentSpeedPt > 90d)
                                        {
                                            currentSpeedPt -= 90d;

                                            if (MyAPIGateway.Utilities.IsDedicated)
                                            {
                                                if (WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(drive.Block))
                                                    WarpDriveSession.Instance.warpDrivesSpeeds[drive.Block] = (new double[] { currentSpeedPt, pitch, yaw, roll });
                                            }
                                            else if (!MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer)
                                            {
                                                if (WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(drive.Block))
                                                    WarpDriveSession.Instance.warpDrivesSpeeds[drive.Block] = (new double[] { currentSpeedPt, pitch, yaw, roll });
                                            }
                                            else if (!MyAPIGateway.Utilities.IsDedicated && !MyAPIGateway.Multiplayer.IsServer)
                                            {
                                                if (WarpDriveSession.Instance.warpDrivesSpeeds.ContainsKey(drive.Block))
                                                    WarpDriveSession.Instance.warpDrivesSpeeds[drive.Block] = (new double[] { currentSpeedPt, pitch, yaw, roll });

                                                WarpDriveSession.Instance.TransmitWarpSpeed(drive.Block, currentSpeedPt, pitch, yaw, roll);
                                            }
                                        }
                                        else
                                        {
                                            hasEnoughPower = false;
                                            drive.RequiredPower = totalPower / functionalDrives;
                                            return;
                                        }
                                    }
                                }
                                drive.RequiredPower = totalPower / functionalDrives;
                            }
                            else
                            {
                                if (drive.RequiredPower != 0)
                                    drive.RequiredPower = 0;
                            }
                        }
                    }
                    else
                    {
                        if (drive.RequiredPower != 0)
                            drive.RequiredPower = 0;
                    }
                }

                functionalDrives = numFunctional;

                if (WarpState == State.Active)
                    totalHeat += WarpDrive.Instance.Settings.heatGain;
                else
                    totalHeat -= WarpDrive.Instance.Settings.heatDissipationDrive * numFunctional;

                if (!MyAPIGateway.Utilities.IsDedicated)
                {
                    if (totalHeat <= 0)
                    {
                        totalHeat = 0;
                        DriveHeat = 0;
                    }
                    else
                        DriveHeat = (int)(totalHeat / WarpDrive.Instance.Settings.maxHeat * 100);
                }

                if (totalHeat <= 0)
                    totalHeat = 0;

                if (WarpState == State.Charging && grid.MainGrid != null)
                {
                    int percentHeat = (int)(totalHeat / WarpDrive.Instance.Settings.maxHeat * 100);
                    var ElapsedTime = Math.Abs(WarpDriveSession.Instance.Runtime - startChargeRuntime) / 60;

                    var MaxSecondsToWarp = IsPrototech ? WarpDrive.Instance.Settings.PrototechJump : WarpDrive.Instance.Settings.DelayJump;
                    var SecondsToWarp = 0.0;
                    string display = "";
                    string font = "White";

                    if (WarpDrive.Instance.Settings.AllowToDetectEnemyGrids && WarpDrive.Instance.EnemyProxymityDangerCharge(grid.MainGrid))
                    {
                        MaxSecondsToWarp = WarpDrive.Instance.Settings.DelayJumpIfEnemyIsNear;
                        SecondsToWarp = MaxSecondsToWarp - ElapsedTime;

                        font = "Red";
                        if (percentHeat > 0)
                            display += $"Enemy Detected!!!\nHeat: {percentHeat}%\n";

                        display += $"Power Usage: {totalPower:F2} MW\nSeconds to Warp: {SecondsToWarp}\n";

                        string s1 = WarpDrive.Instance.ProxymityDangerInWarp(gridMatrix, grid.MainGrid, ((IMyCubeGrid)grid.MainGrid).WorldVolume.Radius, 1500d, controlledCockpit);
                        if (s1 != null)
                            display += "Warp channel blocked by " + s1 + "\n";

                    }
                    else
                    {
                        SecondsToWarp = MaxSecondsToWarp - ElapsedTime;
                        if (percentHeat > 0)
                            display += $"Heat: {percentHeat}%\n";

                        display += $"Power Usage: {totalPower:F2} MW\nSeconds to Warp: {SecondsToWarp}\n";

                        string s1 = WarpDrive.Instance.ProxymityDangerInWarp(gridMatrix, grid.MainGrid, ((IMyCubeGrid)grid.MainGrid).WorldVolume.Radius, 1500d, controlledCockpit);
                        if (s1 != null)
                        {
                            font = "Red";
                            display += "Warp channel blocked by " + s1 + "\n";
                        }


                    }

                    if (percentHeat >= 65)
                        font = "Red";
                    if (percentHeat >= 75)
                        display += '!';
                    if (percentHeat >= 85)
                        display += '*';
                    if (percentHeat >= 90)
                        display += '*';
                    if (percentHeat >= 95)
                        display += '*';

                    if (MyAPIGateway.Utilities.IsDedicated)
                    {
                        if (_updateTicks++ >= 61)
                        {
                            SendMessage(display, 1f, font);
                            _updateTicks = 0;
                        }
                    }
                    else
                    {
                        if (_updateTicks++ >= 62)
                        {
                            SendMessage(display, 1f, font);
                            _updateTicks = 0;
                        }
                    }
                }

                if (WarpState == State.Active)
                {
                    if (totalHeat > 0)
                    {
                        int percentHeat = (int)(totalHeat / WarpDrive.Instance.Settings.maxHeat * 100);
                        string display = $"Heat: {percentHeat}%\n";

                        if (totalPower < 1000f)
                            display += $"Power Usage: {totalPower:F2} MW";
                        else
                            display += $"Power Usage: {(totalPower / 1000f):F2} GW";

                        string font = "White";
                        if (percentHeat >= 75)
                            display += '!';
                        if (percentHeat >= 85)
                        {
                            display += '*';
                            font = "Red";
                        }
                        if (percentHeat >= 90)
                            display += '*';
                        if (percentHeat >= 95)
                            display += '*';

                        string msg = "";
                        double speedInKm_s = currentSpeedPt * 60 / 1000;
                        float currentGravity = GridGravityNow();
                        if (currentGravity > 0d)
                            msg += updateMyShowHorizonIndicator(false, currentGravity);

                        if (speedInKm_s < 1000d)                                     // Shows up to 999 km/s
                            msg += $"Speed: {speedInKm_s:0} km/s\n{display}";
                        else if (speedInKm_s < 299792d)                              // Shows up to 299,12 Mm/s
                            msg += $"Speed: {speedInKm_s / 1000d:F2} Mm/s\n{display}";
                        else                                                         // Shows everything else in 3,123 c
                            msg += $"Speed: {speedInKm_s / 299792d:F3} c\n{display}";

                        if (MyAPIGateway.Utilities.IsDedicated)
                        {
                            if (_updateTicks++ >= 61)
                            {
                                SendMessage(msg, 1f, font);
                                _updateTicks = 0;
                            }
                        }
                        else
                        {
                            if (_updateTicks++ >= 62)
                            {
                                SendMessage(msg, 1f, font);
                                _updateTicks = 0;
                            }
                        }
                    }
                    else
                    {
                        string msg = "";
                        double speedInKm_s = currentSpeedPt * 60 / 1000;

                        float currentGravity = GridGravityNow();
                        if (currentGravity > 0d)
                            msg += updateMyShowHorizonIndicator(false, currentGravity);

                        if (speedInKm_s < 1000d)                             // Shows up to 999 km/s
                            msg += $"Speed: {speedInKm_s:0} km/s\n";
                        else if (speedInKm_s < 299792d)                      // Shows up to 299,12 Mm/s
                            msg += $"Speed: {speedInKm_s / 1000d:F2} Mm/s\n";
                        else                                                 // Shows everything else in 3,123 c
                            msg += $"Speed: {speedInKm_s / 299792d:F3} c\n";

                        if (totalPower < 1000d)
                            msg += $"Power Usage: {totalPower:F2} MW";
                        else
                            msg += $"Power Usage: {(totalPower / 1000f):F2} GW";

                        if (MyAPIGateway.Utilities.IsDedicated)
                        {
                            if (_updateTicks++ >= 61)
                            {
                                SendMessage(msg, 1f, "White");
                                _updateTicks = 0;
                            }
                        }
                        else
                        {
                            if (_updateTicks++ >= 62)
                            {
                                SendMessage(msg, 1f, "White");
                                _updateTicks = 0;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Sets the vanilla horizon from cockpits 
        /// (ToDo: Other players in warp?)
        /// </summary>
        /// <param name="vanillaHorizonOn">true vanialla horizon on</param>
        private void updateMyShowHorizonIndicator(bool vanillaHorizonOn)
        {
            controlledCockpit.ShowHorizonIndicator = vanillaHorizonOn;
            // on off for other player
        }

        /// <summary>
        /// Get additional altitude and gravity to display
        /// </summary>
        /// <param name="vanillaHorizonOn">true vanialla horizon on</param>
        /// <param name="msg">asks for string to print</param>
        /// <returns>string, msg to add to hud</returns>
        private string updateMyShowHorizonIndicator(bool vanillaHorizonOn, float currentGravity)
        {
            updateMyShowHorizonIndicator(vanillaHorizonOn);
            // Display myHorizon
            double elevation = getElevation();
            return $"Altitude: {getElevation():0} m   Gravity: {currentGravity:F2} g\n";
        }
        private void PlayParticleEffect()
        {
            if (effect != null)
            {
                effect.Play();
                return;
            }

            if (grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;
            Vector3D forward = gridMatrix.Forward;
            MatrixD fromDir = MatrixD.CreateFromDir(-forward);
            Vector3D origin = MainGrid.PositionComp.WorldAABB.Center;
            Vector3D effectOffset = forward * MainGrid.PositionComp.WorldAABB.HalfExtents.AbsMax() * 2.0;
            fromDir.Translation = MainGrid.PositionComp.WorldAABB.Center + effectOffset;

            var IGrid = MainGrid as IMyCubeGrid;
            float gridWidth = IGrid.LocalAABB.Width > IGrid.LocalAABB.Height ? IGrid.LocalAABB.Width : IGrid.LocalAABB.Height;
            float scale = gridWidth / 30;

            if (MainGrid.GridSizeEnum == MyCubeSize.Large)
                scale = gridWidth / 60;

            if (IsPrototech)
            {
                MyParticlesManager.TryCreateParticleEffect("Warp_Slipspace", ref fromDir, ref origin, uint.MaxValue, out effect);
            }
            else
            {
                MyParticlesManager.TryCreateParticleEffect("Warp_Slipspace", ref fromDir, ref origin, uint.MaxValue, out effect);
            }

            if (effect != null)
                effect.UserScale = scale;
        }

        private void UpdateParticleEffect()
        {
            if (effect == null || effect.IsStopped || grid.MainGrid == null)
                return;

            var MainGrid = grid.MainGrid;
            Vector3D forward = gridMatrix.Forward;
            Vector3D effectOffset = forward * MainGrid.PositionComp.WorldAABB.HalfExtents.AbsMax() * 2.0;
            Vector3D origin = MainGrid.PositionComp.WorldAABB.Center + effectOffset;

            effect.SetTranslation(ref origin);
        }

        private void StopParticleEffect()
        {
            if (effect == null)
                return;

            effect.StopEmitting(10f);
            effect = null;
        }

        private void StopParticleEffectNow()
        {
            if (effect == null)
                return;

            effect.Stop();
            effect = null;
        }

        public float CulcucateGridGlobalMass(IMyCubeGrid Grid)
        {
            float GlobalMass = 1f;

            float mass;
            float physicalMass;
            float currentMass = 0;
            var MyGrid = Grid as MyCubeGrid;

            if (MyGrid != null)
                currentMass = MyGrid.GetCurrentMass(out mass, out physicalMass, GridLinkTypeEnum.Physical);

            if (currentMass > 0)
                GlobalMass = currentMass;

            return GlobalMass;
        }

        private void OnSystemInvalidated(GridSystem system)
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                sound?.StopSound(true);
                effect?.Stop();
                BlinkTrailEffect?.Stop();
            }
            OnSystemInvalidatedAction?.Invoke(this);
            OnSystemInvalidatedAction = null;
        }

        public void SendMessage(string msg, float seconds = 5, string font = "Red", long PlayerID = 0L)
        {
            var Hostplayer = MyAPIGateway.Session?.Player;
            var cockpit = Hostplayer?.Character?.Parent as IMyShipController;

            if (OnlinePlayersList != null && OnlinePlayersList.Count > 0 && PlayerID > 0)
            {
                foreach (var SelectedPlayer in OnlinePlayersList)
                {
                    if (SelectedPlayer.IdentityId == PlayerID)
                    {
                        MyVisualScriptLogicProvider.ShowNotification(msg, (int)(seconds * 1000), font, SelectedPlayer.IdentityId);
                        return;
                    }
                }
            }

            if (Hostplayer != null && cockpit?.CubeGrid != null && grid.Contains((MyCubeGrid)cockpit.CubeGrid))
                MyVisualScriptLogicProvider.ShowNotification(msg, (int)(seconds * 1000), font, Hostplayer.IdentityId);

            if (OnlinePlayersList != null && OnlinePlayersList.Count > 0)
            {
                foreach (var ClientPlayer in OnlinePlayersList)
                {
                    if (Hostplayer != null && ClientPlayer.IdentityId == Hostplayer.IdentityId)
                        continue;

                    var ClientCockpit = ClientPlayer?.Character?.Parent as IMyShipController;

                    if (ClientCockpit?.CubeGrid != null && grid.Contains((MyCubeGrid)ClientCockpit.CubeGrid))
                        MyVisualScriptLogicProvider.ShowNotification(msg, (int)(seconds * 1000), font, ClientPlayer.IdentityId);
                }
            }
        }

        private void OnDriveAdded(IMyCubeBlock block)
        {
            WarpDrive drive = block.GameLogic.GetAs<WarpDrive>();
            HashSet<WarpDrive> gridDrives;
            drive.SetWarpSystem(this);

            if (!warpDrives.TryGetValue(block.CubeGrid, out gridDrives))
                gridDrives = new HashSet<WarpDrive>();

            gridDrives.Add(drive);
            warpDrives[block.CubeGrid] = gridDrives;
        }

        private void OnDriveRemoved(IMyCubeBlock block)
        {
            WarpDrive drive = block.GameLogic.GetAs<WarpDrive>();
            HashSet<WarpDrive> gridDrives;

            if (warpDrives.TryGetValue(block.CubeGrid, out gridDrives))
            {
                gridDrives.Remove(drive);

                if (GridsMass.ContainsKey(drive.Block.CubeGrid.EntityId))
                    GridsMass.Remove(drive.Block.CubeGrid.EntityId);

                if (gridDrives.Count > 0)
                    warpDrives[block.CubeGrid] = gridDrives;
                else
                    warpDrives.Remove(block.CubeGrid);
            }
        }

        public override bool Equals(object obj)
        {
            var system = obj as WarpSystem;
            return system != null && Id == system.Id;
        }

        public override int GetHashCode()
        {
            return 2108858624 + Id.GetHashCode();
        }

        public enum State
        {
            Idle, Charging, Active
        }

        #region DEBUGGING
        private void DEBUGGING_Collision_onCharge()
        {

            IMyCubeGrid warpGrid = (IMyCubeGrid)grid.MainGrid;

            List<IMyEntity> entList = new List<IMyEntity>();
            List<IMyCubeGrid> attachedList = new List<IMyCubeGrid>();
            Vector3D center = warpGrid.WorldVolume.Center;
            float mass = warpGrid.Physics.Mass; // landinggear connected?!
            double radius = warpGrid.WorldVolume.Radius + (mass / 100000);
            // ToDo: Add mass to radius -> calc's needed

            BoundingSphereD sphere = new BoundingSphereD(center, radius);

            // There will be at least ONE in this list, the ship itself
            entList = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);

            // Get all subgrids grids and locked on landing gear.
            MyAPIGateway.GridGroups.GetGroup(warpGrid, GridLinkTypeEnum.Physical, attachedList);

            // Check if the entities' boundingboxes hit the warp bubble
            foreach (IMyEntity ent in entList)
            {
                //if (ent is MySafeZone)                  
                //   return "SafeZone";

                // Shall we ignore characters? What about missiles? bullets? (We should ignore floating objects)
                if (!(ent is MyCubeGrid || ent is MyVoxelMap))
                    continue;

                if (ent is IMyCubeGrid)
                {
                    // Skip own MainGrid
                    IMyCubeGrid foundGrid = (IMyCubeGrid)ent;
                    if (attachedList.Contains(foundGrid))
                        continue;

                    // Not own grid?
                    string msg = ent.DisplayName;
                    string cut_msg = msg.Length > 12 ? msg.Substring(0, 12) : msg;      // want a max string of 12 to display
                    //return cut_msg;
                    SendMessage("Debug_Grid: " + cut_msg, 0.016f);
                }

                if (ent is MyVoxelMap)
                {
                    // How to make collision detection even more precise?
                    // BoundingBoxEffect? -> asteroids have a big BB ....
                    // Lore: Dust around asteorids prevent establishing a warp bubble.

                    MyVoxelMap vMap = (MyVoxelMap)ent;
                    string msg = (vMap.StorageName).Split('_')[0];                      // Sanitize the sting Asteroid-2342343434 ?!
                    string cut_msg = msg.Length > 12 ? msg.Substring(0, 12) : msg;      // want a max string of 12 to display
                    SendMessage("Debug: " + cut_msg, 0.016f);
                    //return cut_msg;
                }
            }
            DrawSphere(sphere, Color.Red);
            //return null;

        }
        private void DEBUGGING_Collision_inWarp()
        {
            if (grid == null)
                return;

            IMyCubeGrid mainGrid = grid.MainGrid;

            List<IMyCubeGrid> attachedGrid = new List<IMyCubeGrid>();           // All conntected grids
            List<IMyEntity> entList = new List<IMyEntity>();                    // Possible entities to check for collision
            Vector3D start = mainGrid.WorldVolume.Center;                       // grid.WorldVolume.Center; // or ideally the whole construct's volume, spheres can be Include()'d
            float mass = mainGrid.Physics.Mass;
            double radius = mainGrid.WorldVolume.Radius + (mass / 100000d);     // more mass == more warp bubble
            double length = 250 + 500;

            Vector3D forward = controlledCockpit.WorldMatrix.Forward;           /* velocity normalized */
            Vector3D up = controlledCockpit.WorldMatrix.Up;                     /* velocity normalized */

            double halfLength = length * 0.5;
            Vector3D center = start + forward * halfLength;
            Vector3D halfExtents = new Vector3D(radius, radius, halfLength);

            Quaternion quat = Quaternion.CreateFromRotationMatrix(MatrixD.CreateFromDir(forward)); // a bit overkill, maybe you can find a proper up vector for CreateFromForwardUp()
            //Quaternion quat = Quaternion.CreateFromForwardUp(forward, up); // could work, ToDo check

            MyOrientedBoundingBoxD obb = new MyOrientedBoundingBoxD(center, halfExtents, quat);

            //entList = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);

            BoundingBoxD bb = obb.GetAABB();
            entList = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref bb);

            //SendMessage("Count_" + entList.Count, 0.016f);
            SendMessage("entList " + entList.Count, 0.016f);

            DrawOBB(obb, Color.Red);

            /*IMyCubeGrid WarpGrid = grid.MainGrid;

            IMyEntity cockpit = controlledCockpit.Parent;
            if (cockpit == null)
                return;

            List<IMyEntity> entList;

            //DrawSphere(sphere, Color.Red);


            IMyCubeGrid gd = grid.MainGrid;

            Vector3D forward = controlledCockpit.WorldMatrix.Forward; velocity normalized 
            double length = 1000; length of the shape
            Vector3D start = gd.WorldVolume.Center; //grid.WorldVolume.Center; // or ideally the whole construct's volume, spheres can be Include()'d
            double radius = gd.WorldVolume.Radius;

            double halfLength = length * 0.5;

            Vector3D center = start + forward * halfLength;
            Vector3D halfExtents = new Vector3D(radius, radius, halfLength);

            var quat = Quaternion.CreateFromRotationMatrix(MatrixD.CreateFromDir(forward)); // a bit overkill, maybe you can find a proper up vector for CreateFromForwardUp()

            MyOrientedBoundingBoxD obb = new MyOrientedBoundingBoxD(center, halfExtents, quat);

            //entList = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);

            BoundingBoxD box = obb.GetAABB();
            entList = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref box);

            SendMessage("Count_" + entList.Count, 0.016f);
            DrawOBB(obb, Color.Red);*/

        }
        private void DrawSphere(BoundingSphereD sphere, Color color, MySimpleObjectRasterizer draw = MySimpleObjectRasterizer.SolidAndWireframe, BlendTypeEnum blend = BlendTypeEnum.PostPP)
        {
            MatrixD wm = MatrixD.CreateTranslation(sphere.Center);
            MySimpleObjectDraw.DrawTransparentSphere(ref wm, (float)sphere.Radius, ref color, draw, 24, null, null, 0.01f, blendType: blend);
        }
        private void DrawOBB(MyOrientedBoundingBoxD obb, Color color, MySimpleObjectRasterizer draw = MySimpleObjectRasterizer.SolidAndWireframe, BlendTypeEnum blend = BlendTypeEnum.PostPP, bool extraSeeThrough = true)
        {
            MatrixD wm = MatrixD.CreateFromQuaternion(obb.Orientation);
            wm.Translation = obb.Center;

            BoundingBoxD localBB = new BoundingBoxD(-obb.HalfExtent, obb.HalfExtent);

            MySimpleObjectDraw.DrawTransparentBox(ref wm, ref localBB, ref color, draw, 1, faceMaterial: null, lineMaterial: null, blendType: blend);
        }
        #endregion

    }


    public class MechItem
    {
        public IMyMechanicalConnectionBlock basis { get; private set; }
        public IMyAttachableTopBlock top { get; private set; }

        public MechItem(IMyMechanicalConnectionBlock _basis, IMyAttachableTopBlock _top)
        {
            basis = _basis;
            top = _top;
        }

        public void Attach(bool updateGroup = true)
        {
            basis.Attach(top, updateGroup);
        }
    }
}
