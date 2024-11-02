using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using System.Collections.Generic;
using System.Text;
using ProtoBuf;
using VRage.Game.ModAPI;
using VRage.Game;
using System;
using Sandbox.Game.EntityComponents;
using VRage.ModAPI;
using Sandbox.ModAPI.Ingame;
using IMyShipController = Sandbox.ModAPI.IMyShipController;
using IMyBatteryBlock = Sandbox.ModAPI.IMyBatteryBlock;
using Sandbox.Game.GameSystems.Electricity;
using VRage.Game.ObjectBuilders.Definitions;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace TeleportMechanisms {
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_BatteryBlock), false,
        "RingwayCore", "SmallRingwayCore")]
    public class TeleportGateway : MyGameLogicComponent {
        public TeleportGatewaySettings Settings { get; private set; } = new TeleportGatewaySettings();
        public IMyBatteryBlock RingwayBlock;

        private static bool _controlsCreated = false;
        private static readonly Guid StorageGuid = new Guid("7F995845-BCEF-4E37-9B47-A035AC2A8E0B");

        private const int SAVE_INTERVAL_FRAMES = 100;
        private int _frameCounter = 0;

        private int _linkUpdateCounter = 0;
        private const int LINK_UPDATE_INTERVAL = 1;


        private MyResourceSinkComponent Sink = null;
        private bool _isTeleporting = false;
        private int _teleportCountdown = 0;
        private double _jumpDistance = 0;

        private const float POWER_THRESHOLD = 0.1f; // 10% power threshold for failure
        private const float BASE_COUNTDOWN_SECONDS = 5; // Minimum countdown time
        private const float SECONDS_PER_100KM = 1.0f; // Additional second per 100km
        private const float POWER_PER_100KM = 1.0f; // 1 MWh per 100km

        static TeleportGateway() {
            CreateControls();
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
            RingwayBlock = Entity as IMyBatteryBlock;
            if (RingwayBlock == null) {
                MyLogger.Log($"TPGate: Init: Entity is not a terminal block. EntityId: {Entity?.EntityId}");
                return;
            }

            Settings = Load(RingwayBlock);
            RingwayBlock.AppendingCustomInfo += AppendingCustomInfo;

            // Initialize power sink
            Sink = RingwayBlock.Components.Get<MyResourceSinkComponent>();
            if (Sink != null) {
                Sink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, ComputeRequiredPower);
                MyLogger.Log($"TPGate: Init: Power sink initialized for EntityId: {RingwayBlock.EntityId}");
            }

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

            lock (TeleportCore._lock) {
                TeleportCore._instances[RingwayBlock.EntityId] = this;
            }
        }

        private float ComputeRequiredPower() {
            if (!_isTeleporting || RingwayBlock == null) return 0f;

            float powerRequired = CalculatePowerRequired(_jumpDistance);
            float powerPerSecond = powerRequired / (_teleportCountdown / 60f);
            return powerPerSecond * 1000f; // Convert to watts
        }

        public override void UpdateOnceBeforeFrame() {
            if (RingwayBlock == null || RingwayBlock.CubeGrid?.Physics == null) return;

            var battery = RingwayBlock as IMyBatteryBlock;
            if (battery == null) return;

            battery.ChargeMode = ChargeMode.Recharge; // Set to recharge mode by default
            battery.IsWorkingChanged += Battery_IsWorkingChanged;

        }

        private void Battery_IsWorkingChanged(IMyCubeBlock obj) {
            var battery = obj as IMyBatteryBlock;
            if (battery == null || obj.EntityId != RingwayBlock.EntityId) return;

            if (battery.ChargeMode != ChargeMode.Recharge) {
                MyLogger.Log($"TPGate: IsWorkingChanged - Forcing recharge mode. Was: {battery.ChargeMode}");
                battery.ChargeMode = ChargeMode.Recharge;
            }
        }


        private bool ConsumeAllPower() {
            var battery = RingwayBlock as IMyBatteryBlock;
            if (battery == null) {
                MyLogger.Log($"TPGate: ConsumeAllPower: RingwayBlock is not a battery!");
                return false;
            }

            // Check if the battery is at 100% charge (allowing for small margin)
            if (battery.CurrentStoredPower < battery.MaxStoredPower * 0.99) {
                MyLogger.Log($"TPGate: ConsumeAllPower: Battery not fully charged. Current: {battery.CurrentStoredPower}, Max: {battery.MaxStoredPower}");
                return false;
            }

            float initialCharge = battery.CurrentStoredPower;

            // Set an extremely high power requirement to drain instantly
            Sink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, battery.MaxStoredPower * 1000);
            Sink.Update();

            // Reset the power requirement
            Sink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, 0f);
            Sink.Update();

            float consumedPower = initialCharge - battery.CurrentStoredPower;
            MyLogger.Log($"TPGate: ConsumeAllPower: Consumed {consumedPower} MW for jump. Remaining: {battery.CurrentStoredPower} MW");

            return true;
        }

        private void AppendingCustomInfo(IMyTerminalBlock block, StringBuilder sb) {
            try {
                var battery = block as IMyBatteryBlock;

                if (!block.IsWorking) {
                    sb.Append("--- Gateway Offline ---\n");
                    sb.Append("The gateway is not functional. Check power and block integrity.\n");
                    return;
                }

                sb.Append("--- Teleport Gateway Status ---\n");
                if (battery != null)
                {
                    float chargePercentage = (float)(battery.CurrentStoredPower / battery.MaxStoredPower * 100);
                    sb.Append($"Charge: {chargePercentage:F1}% ({battery.CurrentStoredPower:F2}/{battery.MaxStoredPower:F2} MWh)\n");
                    sb.Append(chargePercentage >= 99 ? "Status: Ready to Jump\n" : "Status: Charging...\n");
                }

                var linkedGateways = TeleportCore._TeleportLinks.ContainsKey(Settings.GatewayName)
                    ? TeleportCore._TeleportLinks[Settings.GatewayName]
                    : new List<long>();

                int linkedCount = linkedGateways.Count;
                sb.Append($"Linked Gateways: {linkedCount}\n");

                if (linkedCount > 2) {
                    sb.Append($"WARNING: More than two gateways on channel '{Settings.GatewayName}'.\n");
                    sb.Append("         Only the nearest gateway will be used.\n");
                }

                if (linkedCount > 0) {
                    sb.Append("Linked To:\n");
                    var sourcePosition = RingwayBlock.GetPosition();

                    IMyBatteryBlock nearestGateway = null;
                    double nearestDistance = double.MaxValue;

                    foreach (var gatewayId in linkedGateways) {
                        if (gatewayId != RingwayBlock.EntityId) {
                            var linkedGateway = MyAPIGateway.Entities.GetEntityById(gatewayId) as IMyBatteryBlock;
                            if (linkedGateway != null) {
                                var distance = Vector3D.Distance(sourcePosition, linkedGateway.GetPosition());
                                string distanceStr = $"{distance / 1000:F1} km";

                                if (distance < nearestDistance) {
                                    nearestDistance = distance;
                                    nearestGateway = linkedGateway;
                                }

                                sb.Append($"  - {linkedGateway.CustomName}: {distanceStr}\n");
                            }
                            else {
                                sb.Append($"  - Unknown (ID: {gatewayId})\n");
                            }
                        }
                    }

                    if (nearestGateway != null && linkedCount > 1) {
                        sb.Append($"Active Destination: {nearestGateway.CustomName}\n");
                    }
                }
                else {
                    sb.Append("Status: Not linked to any other gateways\n");
                }

                // Settings Info (unchanged)
                sb.Append($"Allow Players: {(Settings.AllowPlayers ? "Yes" : "No")}\n");
                sb.Append($"Allow Ships: {(Settings.AllowShips ? "Yes" : "No")}\n");
                sb.Append($"Show Sphere: {(Settings.ShowSphere ? "Yes" : "No")}\n");
                sb.Append($"Sphere Diameter: {Settings.SphereDiameter} m\n");
            }
            catch (Exception e) {
                MyLog.Default.WriteLineAndConsole($"Error in AppendingCustomInfo: {e}");
            }
        }

        private float _targetPowerDrain = 0f;
        private float _initialPower;

        public override void UpdateAfterSimulation() {
            base.UpdateAfterSimulation();

            var battery = RingwayBlock as IMyBatteryBlock;
            if (battery == null) return;

            if (_isTeleporting) {
                if (battery.ChargeMode != ChargeMode.Discharge) {
                    battery.ChargeMode = ChargeMode.Discharge;
                }

                float powerRequired = CalculatePowerRequired(_jumpDistance);
                // Calculate massive power draw per second (100x the total required power)
                float powerDrawPerSecond = (powerRequired * 100f) / (_teleportCountdown / 60f);

                // Update sink with massive power requirement
                if (Sink != null) {
                    Sink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, powerDrawPerSecond * 1000f); // Convert to watts
                    Sink.Update();
                }

                float currentPowerPercentage = battery.CurrentStoredPower / battery.MaxStoredPower;

                _teleportCountdown--;

                if (_teleportCountdown % 60 == 0) {
                    int secondsLeft = _teleportCountdown / 60;
                    MyAPIGateway.Utilities.ShowNotification(
                        $"Jump in {secondsLeft}s... Distance: {_jumpDistance / 1000:F1}km, Power: {battery.CurrentStoredPower:F1}/{powerRequired:F1}MWh",
                        50,
                        MyFontEnum.White
                    );
                }

                // Check if power is too low or countdown finished
                if (battery.CurrentStoredPower < powerRequired * POWER_THRESHOLD || _teleportCountdown <= 0) {
                    _isTeleporting = false;
                    battery.ChargeMode = ChargeMode.Recharge;

                    // Reset sink
                    if (Sink != null) {
                        Sink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, 0f);
                        Sink.Update();
                    }

                    if (battery.CurrentStoredPower < powerRequired * POWER_THRESHOLD) {
                        MyAPIGateway.Utilities.ShowNotification(
                            $"Jump failed - Power depleted during charging",
                            2000,
                            MyFontEnum.Red
                        );
                        return;
                    }

                    // Execute actual teleport
                    if (!MyAPIGateway.Multiplayer.IsServer) {
                        var message = new JumpRequestMessage {
                            GatewayId = battery.EntityId,
                            Link = Settings.GatewayName
                        };
                        MyAPIGateway.Multiplayer.SendMessageToServer(
                            NetworkHandler.JumpRequestId,
                            MyAPIGateway.Utilities.SerializeToBinary(message)
                        );
                    }
                    else {
                        ProcessJumpRequest(battery.EntityId, Settings.GatewayName);
                    }
                }
            }
            else if (battery.ChargeMode != ChargeMode.Recharge) {
                battery.ChargeMode = ChargeMode.Recharge;

                // Ensure sink is reset when not teleporting
                if (Sink != null) {
                    Sink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, 0f);
                    Sink.Update();
                }
            }

            if (++_frameCounter >= SAVE_INTERVAL_FRAMES) {
                _frameCounter = 0;
                TrySave();
            }

            if (MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel) {
                RingwayBlock.RefreshCustomInfo();
                RingwayBlock.SetDetailedInfoDirty();
            }

            if (!MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session != null) {
                TeleportBubbleManager.CreateOrUpdateBubble(RingwayBlock);
                TeleportBubbleManager.DrawBubble(RingwayBlock);
            }
        }

        public override void UpdateAfterSimulation100() {
            if (!RingwayBlock.IsWorking) return;

            try {

                // New link update logic
                if (++_linkUpdateCounter >= LINK_UPDATE_INTERVAL) {
                    _linkUpdateCounter = 0;
                    TeleportCore.UpdateTeleportLinks();
                    MyLogger.Log("TPGate: UpdateAfterSimulation100: Updated teleport links");
                }

                // Refresh custom info only when the terminal is open
                if (MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel) {
                    RingwayBlock.RefreshCustomInfo();
                    RingwayBlock.SetDetailedInfoDirty();
                }
            }
            catch (Exception e) {
                MyLogger.Log($"TPGate: UpdateAfterSimulation100: Exception - {e}");
            }
        }

        private void CancelTeleport() {
            if (_isTeleporting) {
                _isTeleporting = false;
                _teleportCountdown = 0;

                var battery = RingwayBlock as IMyBatteryBlock;
                if (battery != null) {
                    battery.ChargeMode = ChargeMode.Recharge;
                }

                // Reset sink
                if (Sink != null) {
                    Sink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, 0f);
                    Sink.Update();
                }

                MyAPIGateway.Utilities.ShowNotification("Teleport sequence cancelled", 2000, MyFontEnum.Red);
            }
        }

        private void TrySave() {
            if (!Settings.Changed) return;

            Save();
            MyLogger.Log($"TPGate: TrySave: Settings saved for EntityId: {RingwayBlock.EntityId}");
        }

        private void Save() {
            if (RingwayBlock.Storage == null) {
                RingwayBlock.Storage = new MyModStorageComponent();
            }

            string serializedData = MyAPIGateway.Utilities.SerializeToXML(Settings);
            RingwayBlock.Storage.SetValue(StorageGuid, serializedData);

            // Send the updated settings to the server
            var message = new SyncSettingsMessage { EntityId = RingwayBlock.EntityId, Settings = this.Settings };
            var data = MyAPIGateway.Utilities.SerializeToBinary(message);
            MyAPIGateway.Multiplayer.SendMessageToServer(NetworkHandler.SyncSettingsId, data);

            Settings.Changed = false;
            Settings.LastSaved = MyAPIGateway.Session.ElapsedPlayTime;
            MyLogger.Log($"TPGate: Save: Settings saved for EntityId: {RingwayBlock.EntityId}");
        }


        public void ApplySettings(TeleportGatewaySettings settings) {
            this.Settings = settings;
            MyLogger.Log($"TPGate: ApplySettings: Applied settings for EntityId: {RingwayBlock.EntityId}, GatewayName: {Settings.GatewayName}");
        }

        private static TeleportGatewaySettings Load(IMyBatteryBlock block) {
            MyLogger.Log($"TPGate: Load: Called. Attempting to load with StorageGuid: {StorageGuid}");
            if (block == null) {
                MyLogger.Log($"TPGate: Load: RingwayBlock is null.");
                return new TeleportGatewaySettings();
            }
            if (block.Storage == null) {
                MyLogger.Log($"TPGate: Load: RingwayBlock Storage is null. Creating new Storage.");
                block.Storage = new MyModStorageComponent();
            }
            MyLogger.Log($"TPGate: Load: RingwayBlock and Storage not null.");
            string data;
            if (block.Storage.TryGetValue(StorageGuid, out data)) {
                MyLogger.Log($"TPGate: Load: blockid:{block.EntityId} Storage had data: {data}");
                try {
                    var settings = MyAPIGateway.Utilities.SerializeFromXML<TeleportGatewaySettings>(data);
                    if (settings != null) {
                        settings.Changed = false;
                        settings.LastSaved = MyAPIGateway.Session.ElapsedPlayTime;
                        MyLogger.Log($"TPGate: Load: Successfully loaded settings.");
                        return settings;
                    }
                    else {
                        MyLogger.Log($"TPGate: Load: Deserialized settings were null.");
                    }
                }
                catch (Exception ex) {
                    MyLogger.Log($"TPGate: Load - Exception loading settings: {ex}");
                }
            }
            else {
                MyLogger.Log($"TPGate: Load: No data found for StorageGuid.");
            }
            MyLogger.Log($"TPGate: Load: Creating and returning new TeleportGatewaySettings.");
            var newSettings = new TeleportGatewaySettings();
            newSettings.Changed = true; // Mark as changed so it will be saved
            return newSettings;
        }

        public override void Close() {
            Save();

            lock (TeleportCore._lock) {
                TeleportCore._instances.Remove(RingwayBlock.EntityId);
                MyLogger.Log($"TPGate: Close: Removed instance for EntityId {Entity.EntityId}. Remaining instances: {TeleportCore._instances.Count}");
            }

            if (RingwayBlock != null) {
                var battery = RingwayBlock as IMyBatteryBlock;
                if (battery != null) {
                    battery.IsWorkingChanged -= Battery_IsWorkingChanged;
                }
            }

            TeleportBubbleManager.RemoveBubble(RingwayBlock);

            base.Close();
        }

        public override bool IsSerialized() {
            Save();
            return base.IsSerialized();
        }

        private static void CreateControls() {
            if (_controlsCreated) return;

            MyLogger.Log("TPGate: CreateControl: Creating custom controls and actions");

            var controls = new List<IMyTerminalControl>
            {
                CreateGatewayNameControl(),
                CreateJumpButton(),
                CreateAllowPlayersCheckbox(),
                CreateAllowShipsCheckbox(),
                CreateShowSphereCheckbox(),
                CreateSphereDiameterSlider()
            };

            var actions = new List<IMyTerminalAction>
            {
               CreateJumpAction(),
               CreateToggleShowSphereAction(),
               CreateShowSphereOnAction(),
               CreateShowSphereOffAction()
            };

            MyAPIGateway.TerminalControls.CustomControlGetter += (block, blockControls) => {
                if (block is IMyBatteryBlock && (block.BlockDefinition.SubtypeName == "RingwayCore" || block.BlockDefinition.SubtypeName == "SmallRingwayCore")) {
                    blockControls.AddRange(controls);
                }
            };

            MyAPIGateway.TerminalControls.CustomActionGetter += (block, blockActions) => {
                if (block is IMyBatteryBlock && (block.BlockDefinition.SubtypeName == "RingwayCore" || block.BlockDefinition.SubtypeName == "SmallRingwayCore")) {
                    blockActions.AddRange(actions);
                }
            };

            _controlsCreated = true;
            MyLogger.Log("TPGate: CreateControl: Custom controls and actions created");
        }

        private static IMyTerminalControl CreateGatewayNameControl() {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyBatteryBlock>("GatewayName");
            control.Title = MyStringId.GetOrCompute("Gateway Name");
            control.Getter = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? new StringBuilder(gateway.Settings.GatewayName) : new StringBuilder();
            };
            control.Setter = (block, value) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) {
                    gateway.Settings.GatewayName = value.ToString();
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                }
            };
            control.SupportsMultipleBlocks = false;
            return control;
        }

        private static IMyTerminalControl CreateAllowPlayersCheckbox() {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBatteryBlock>("AllowPlayers");
            control.Title = MyStringId.GetOrCompute("Allow Players");
            control.Getter = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? gateway.Settings.AllowPlayers : false;
            };
            control.Setter = (block, value) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) {
                    gateway.Settings.AllowPlayers = value;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: AllowPlayers set to {value} for EntityId: {block.EntityId}");
                }
            };
            control.SupportsMultipleBlocks = true;
            return control;
        }

        private static IMyTerminalControl CreateAllowShipsCheckbox() {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBatteryBlock>("AllowShips");
            control.Title = MyStringId.GetOrCompute("Allow Ships");
            control.Getter = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? gateway.Settings.AllowShips : false;
            };
            control.Setter = (block, value) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) {
                    gateway.Settings.AllowShips = value;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: AllowShips set to {value} for EntityId: {block.EntityId}");
                }
            };
            control.SupportsMultipleBlocks = true;
            return control;
        }

        private static IMyTerminalControl CreateJumpButton() {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyBatteryBlock>("JumpButton");
            control.Title = MyStringId.GetOrCompute("Jump");
            control.Visible = (block) => true;
            control.Action = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) gateway.JumpAction(block as IMyBatteryBlock);
            };
            return control;
        }

        private static IMyTerminalAction CreateJumpAction() {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyBatteryBlock>("Jump");
            action.Name = new StringBuilder("Jump");
            action.Icon = @"Textures\GUI\Icons\Actions\Jump.dds";
            action.Action = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) gateway.JumpAction(block as IMyBatteryBlock);
            };
            action.Writer = (b, sb) => sb.Append("Initiate Jump");
            return action;
        }

        private static IMyTerminalControl CreateShowSphereCheckbox() {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyBatteryBlock>("ShowSphere");
            control.Title = MyStringId.GetOrCompute("Show Sphere");
            control.Getter = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? gateway.Settings.ShowSphere : false;
            };
            control.Setter = (block, value) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) {
                    gateway.Settings.ShowSphere = value;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: ShowSphere set to {value} for EntityId: {block.EntityId}");
                }
            };
            control.SupportsMultipleBlocks = true;
            return control;
        }

        private static IMyTerminalControl CreateSphereDiameterSlider() {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyBatteryBlock>("SphereDiameter");
            control.Title = MyStringId.GetOrCompute("Sphere Diameter");
            control.SetLimits(1, 100); // Set the range from 1 to 300
            control.Getter = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? gateway.Settings.SphereDiameter : 50.0f;
            };
            control.Setter = (block, value) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) {
                    gateway.Settings.SphereDiameter = value;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                }
            };
            control.Writer = (block, sb) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) {
                    sb.Append($"{gateway.Settings.SphereDiameter} meters");
                }
            };
            control.SupportsMultipleBlocks = true;
            return control;
        }

        private static IMyTerminalAction CreateToggleShowSphereAction() {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyBatteryBlock>("ToggleShowSphere");
            action.Name = new StringBuilder("Toggle Show Sphere");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds"; // You may want to use a different icon
            action.Action = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) {
                    gateway.Settings.ShowSphere = !gateway.Settings.ShowSphere;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: ShowSphere toggled to {gateway.Settings.ShowSphere} for EntityId: {block.EntityId}");
                }
            };
            action.Writer = (b, sb) => sb.Append(b.GameLogic.GetAs<TeleportGateway>()?.Settings.ShowSphere == true ? "Hide Sphere" : "Show Sphere");
            return action;
        }

        private static IMyTerminalAction CreateShowSphereOnAction() {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyBatteryBlock>("ShowSphereOn");
            action.Name = new StringBuilder("Show Sphere On");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds"; // You may want to use a different icon
            action.Action = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) {
                    gateway.Settings.ShowSphere = true;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: ShowSphere set to true for EntityId: {block.EntityId}");
                }
            };
            action.Writer = (b, sb) => sb.Append("Show Sphere On");
            return action;
        }

        private static IMyTerminalAction CreateShowSphereOffAction() {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyBatteryBlock>("ShowSphereOff");
            action.Name = new StringBuilder("Show Sphere Off");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds"; // You may want to use a different icon
            action.Action = (block) => {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) {
                    gateway.Settings.ShowSphere = false;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: ShowSphere set to false for EntityId: {block.EntityId}");
                }
            };
            action.Writer = (b, sb) => sb.Append("Show Sphere Off");
            return action;
        }

        private int CalculateCountdown(double distanceInMeters) {
            float distanceInKm = (float)(distanceInMeters / 1000);
            float additionalSeconds = (distanceInKm / 100) * SECONDS_PER_100KM;
            float totalSeconds = BASE_COUNTDOWN_SECONDS + additionalSeconds;
            return (int)(totalSeconds * 60); // Convert to ticks (60 ticks per second)
        }

        private float CalculatePowerRequired(double distanceInMeters) {
            float distanceInKm = (float)(distanceInMeters / 1000);
            return (distanceInKm / 100) * POWER_PER_100KM;
        }

        private void JumpAction(IMyBatteryBlock block) {
            MyLogger.Log($"TPGate: JumpAction: Jump action triggered for EntityId: {block.EntityId}");

            var link = Settings.GatewayName;
            if (string.IsNullOrEmpty(link)) {
                MyLogger.Log($"TPGate: JumpAction: No valid link set");
                return;
            }

            var destGatewayId = TeleportCore.GetDestinationGatewayId(link, block.EntityId);
            if (destGatewayId == 0) {
                MyLogger.Log($"TPGate: JumpAction: No valid destination gateway found");
                return;
            }

            // Calculate distance to destination
            var destGateway = MyAPIGateway.Entities.GetEntityById(destGatewayId) as IMyBatteryBlock;
            if (destGateway == null) return;

            _jumpDistance = Vector3D.Distance(block.GetPosition(), destGateway.GetPosition());
            float powerRequired = CalculatePowerRequired(_jumpDistance);

            MyLogger.Log($"TPGate: JumpAction: Distance: {_jumpDistance / 1000:F1}km, Power Required: {powerRequired:F1}MWh");

            if (block.CurrentStoredPower < powerRequired) {
                MyLogger.Log($"TPGate: JumpAction: Not enough power for jump. Required: {powerRequired:F1}MWh, Available: {block.CurrentStoredPower:F1}MWh");
                MyAPIGateway.Utilities.ShowNotification($"Insufficient power for {_jumpDistance / 1000:F1}km jump. Need {powerRequired:F1}MWh", 2000, MyFontEnum.Red);
                return;
            }

            // Start teleport sequence
            _isTeleporting = true;
            _teleportCountdown = CalculateCountdown(_jumpDistance);
            _initialPower = block.CurrentStoredPower;
            block.ChargeMode = ChargeMode.Discharge;

            float totalSeconds = _teleportCountdown / 60f;
            MyAPIGateway.Utilities.ShowNotification(
                $"Initiating {_jumpDistance / 1000:F1}km jump - {totalSeconds:F1} seconds",
                2000,
                MyFontEnum.White
            );
        }

        // New method to process jump requests on the server
        public static void ProcessJumpRequest(long gatewayId, string link) {
            MyLogger.Log($"TPGate: ProcessJumpRequest: Processing jump request for gateway {gatewayId}, link {link}");

            var block = MyAPIGateway.Entities.GetEntityById(gatewayId) as IMyBatteryBlock;
            if (block == null || !block.IsWorking) // Add functional check here
            {
                MyLogger.Log($"TPCore: ProcessJumpRequest: Gateway {gatewayId} is null or not functional");
                return;
            }
            // Update teleport links
            TeleportCore.UpdateTeleportLinks();

            var playerList = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(playerList);

            bool teleportAttempted = false;
            int playersToTeleport = 0;
            int shipsToTeleport = 0;

            foreach (var player in playerList) {
                float sphereRadius = block.GameLogic.GetAs<TeleportGateway>()?.Settings.SphereDiameter / 2.0f ?? 25.0f;
                var distance = Vector3D.Distance(player.GetPosition(), block.GetPosition() + block.WorldMatrix.Forward * sphereRadius);

                if (distance <= sphereRadius) {
                    TeleportCore.RequestTeleport(player.IdentityId, block.EntityId, link);
                    teleportAttempted = true;
                    playersToTeleport++;

                    if (player.Controller.ControlledEntity is IMyShipController) {
                        shipsToTeleport++;
                    }
                }
            }

            var destGatewayId = TeleportCore.GetDestinationGatewayId(link, block.EntityId);
            var destGateway = MyAPIGateway.Entities.GetEntityById(destGatewayId) as IMyBatteryBlock;
            if (destGateway != null) {
                var unpilotedShipsCount = TeleportCore.TeleportNearbyShips(block, destGateway);
                shipsToTeleport += unpilotedShipsCount;
                if (unpilotedShipsCount > 0) {
                    teleportAttempted = true;
                }
            }

            if (teleportAttempted) {
                MyLogger.Log($"TPGate: ProcessJumpRequest: Teleport attempted");
                MyAPIGateway.Utilities.ShowNotification($"TPGate: Teleporting {playersToTeleport} player(s) and {shipsToTeleport} ship(s)", 5000, "White");
            }
        }
    }
}
