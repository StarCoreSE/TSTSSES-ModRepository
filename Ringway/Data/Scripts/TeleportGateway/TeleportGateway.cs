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

namespace TeleportMechanisms
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "RingwayCore", "RingwayCoreSmall")]
    public class TeleportGateway : MyGameLogicComponent
    {
        public IMyCollector Block { get; private set; }
        public TeleportGatewaySettings Settings { get; private set; } = new TeleportGatewaySettings();

        private static bool _controlsCreated = false;
        private static readonly Guid StorageGuid = new Guid("7F995845-BCEF-4E37-9B47-A035AC2A8E0B");

        private const int SAVE_INTERVAL_FRAMES = 100;
        private int _frameCounter = 0;

        static TeleportGateway()
        {
            CreateControls();
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            Block = Entity as IMyCollector;
            if (Block == null)
            {
                MyLogger.Log($"TPGate: Init: Entity is not a terminal block. EntityId: {Entity?.EntityId}");
                return;
            }

            Settings = Load(Block);
            MyLogger.Log($"TPGate: Init: Initialized for EntityId: {Block.EntityId}, GatewayName: {Settings.GatewayName}");

            CreateControls();

            lock (TeleportCore._lock)
            {
                TeleportCore._instances[Block.EntityId] = this;
                MyLogger.Log($"TPGate: Init: Added instance for EntityId {Entity.EntityId}. Total instances: {TeleportCore._instances.Count}");
            }

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();

            if (++_frameCounter >= SAVE_INTERVAL_FRAMES)
            {
                _frameCounter = 0;
                TrySave();
            }

            // Only update and draw the bubble if we're not on a dedicated server and the session is available
            if (!MyAPIGateway.Utilities.IsDedicated && MyAPIGateway.Session != null)
            {
                TeleportBubbleManager.CreateOrUpdateBubble(Block);
                TeleportBubbleManager.DrawBubble(Block);
            }
        }

        private void TrySave()
        {
            if (!Settings.Changed) return;

            Save();
            MyLogger.Log($"TPGate: TrySave: Settings saved for EntityId: {Block.EntityId}");
        }

        private void Save()
        {
            if (Block.Storage == null)
            {
                Block.Storage = new MyModStorageComponent();
            }

            string serializedData = MyAPIGateway.Utilities.SerializeToXML(Settings);
            Block.Storage.SetValue(StorageGuid, serializedData);

            // Send the updated settings to the server
            var message = new SyncSettingsMessage { EntityId = Block.EntityId, Settings = this.Settings };
            var data = MyAPIGateway.Utilities.SerializeToBinary(message);
            MyAPIGateway.Multiplayer.SendMessageToServer(NetworkHandler.SyncSettingsId, data);

            Settings.Changed = false;
            Settings.LastSaved = MyAPIGateway.Session.ElapsedPlayTime;
            MyLogger.Log($"TPGate: Save: Settings saved for EntityId: {Block.EntityId}");
        }


        public void ApplySettings(TeleportGatewaySettings settings)
        {
            this.Settings = settings;
            MyLogger.Log($"TPGate: ApplySettings: Applied settings for EntityId: {Block.EntityId}, GatewayName: {Settings.GatewayName}");
        }

        private static TeleportGatewaySettings Load(IMyCollector block)
        {
            MyLogger.Log($"TPGate: Load: Called. Attempting to load with StorageGuid: {StorageGuid}");
            if (block == null)
            {
                MyLogger.Log($"TPGate: Load: Block is null.");
                return new TeleportGatewaySettings();
            }
            if (block.Storage == null)
            {
                MyLogger.Log($"TPGate: Load: Block Storage is null. Creating new Storage.");
                block.Storage = new MyModStorageComponent();
            }
            MyLogger.Log($"TPGate: Load: Block and Storage not null.");
            string data;
            if (block.Storage.TryGetValue(StorageGuid, out data))
            {
                MyLogger.Log($"TPGate: Load: blockid:{block.EntityId} Storage had data: {data}");
                try
                {
                    var settings = MyAPIGateway.Utilities.SerializeFromXML<TeleportGatewaySettings>(data);
                    if (settings != null)
                    {
                        settings.Changed = false;
                        settings.LastSaved = MyAPIGateway.Session.ElapsedPlayTime;
                        MyLogger.Log($"TPGate: Load: Successfully loaded settings.");
                        return settings;
                    }
                    else
                    {
                        MyLogger.Log($"TPGate: Load: Deserialized settings were null.");
                    }
                }
                catch (Exception ex)
                {
                    MyLogger.Log($"TPGate: Load - Exception loading settings: {ex}");
                }
            }
            else
            {
                MyLogger.Log($"TPGate: Load: No data found for StorageGuid.");
            }
            MyLogger.Log($"TPGate: Load: Creating and returning new TeleportGatewaySettings.");
            var newSettings = new TeleportGatewaySettings();
            newSettings.Changed = true; // Mark as changed so it will be saved
            return newSettings;
        }

        public override void Close()
        {
            Save();

            lock (TeleportCore._lock)
            {
                TeleportCore._instances.Remove(Block.EntityId);
                MyLogger.Log($"TPGate: Close: Removed instance for EntityId {Entity.EntityId}. Remaining instances: {TeleportCore._instances.Count}");
            }

            TeleportBubbleManager.RemoveBubble(Block);

            base.Close();
        }

        public override bool IsSerialized()
        {
            Save();
            return base.IsSerialized();
        }

        private static void CreateControls()
        {
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

            MyAPIGateway.TerminalControls.CustomControlGetter += (block, blockControls) =>
            {
                if (block is IMyCollector && (block.BlockDefinition.SubtypeName == "RingwayCore" || block.BlockDefinition.SubtypeName == "RingwayCoreSmall"))
                {
                    blockControls.AddRange(controls);
                }
            };

            MyAPIGateway.TerminalControls.CustomActionGetter += (block, blockActions) =>
            {
                if (block is IMyCollector && (block.BlockDefinition.SubtypeName == "RingwayCore" || block.BlockDefinition.SubtypeName == "RingwayCoreSmall"))
                {
                    blockActions.AddRange(actions);
                }
            };

            _controlsCreated = true;
            MyLogger.Log("TPGate: CreateControl: Custom controls and actions created");
        }

        private static IMyTerminalControl CreateGatewayNameControl()
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyCollector>("GatewayName");
            control.Title = MyStringId.GetOrCompute("Gateway Name");
            control.Getter = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? new StringBuilder(gateway.Settings.GatewayName) : new StringBuilder();
            };
            control.Setter = (block, value) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null)
                {
                    gateway.Settings.GatewayName = value.ToString();
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                }
            };
            control.SupportsMultipleBlocks = false;
            return control;
        }

        private static IMyTerminalControl CreateAllowPlayersCheckbox()
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyCollector>("AllowPlayers");
            control.Title = MyStringId.GetOrCompute("Allow Players");
            control.Getter = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? gateway.Settings.AllowPlayers : false;
            };
            control.Setter = (block, value) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null)
                {
                    gateway.Settings.AllowPlayers = value;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: AllowPlayers set to {value} for EntityId: {block.EntityId}");
                }
            };
            control.SupportsMultipleBlocks = true;
            return control;
        }

        private static IMyTerminalControl CreateAllowShipsCheckbox()
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyCollector>("AllowShips");
            control.Title = MyStringId.GetOrCompute("Allow Ships");
            control.Getter = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? gateway.Settings.AllowShips : false;
            };
            control.Setter = (block, value) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null)
                {
                    gateway.Settings.AllowShips = value;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: AllowShips set to {value} for EntityId: {block.EntityId}");
                }
            };
            control.SupportsMultipleBlocks = true;
            return control;
        }

        private static IMyTerminalControl CreateJumpButton()
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>("JumpButton");
            control.Title = MyStringId.GetOrCompute("Jump");
            control.Visible = (block) => true;
            control.Action = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) gateway.JumpAction(block as IMyCollector);
            };
            return control;
        }

        private static IMyTerminalAction CreateJumpAction()
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("Jump");
            action.Name = new StringBuilder("Jump");
            action.Icon = @"Textures\GUI\Icons\Actions\Jump.dds";
            action.Action = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null) gateway.JumpAction(block as IMyCollector);
            };
            action.Writer = (b, sb) => sb.Append("Initiate Jump");
            return action;
        }

        private static IMyTerminalControl CreateShowSphereCheckbox()
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyCollector>("ShowSphere");
            control.Title = MyStringId.GetOrCompute("Show Sphere");
            control.Getter = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? gateway.Settings.ShowSphere : false;
            };
            control.Setter = (block, value) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null)
                {
                    gateway.Settings.ShowSphere = value;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: ShowSphere set to {value} for EntityId: {block.EntityId}");
                }
            };
            control.SupportsMultipleBlocks = true;
            return control;
        }

        private static IMyTerminalControl CreateSphereDiameterSlider()
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("SphereDiameter");
            control.Title = MyStringId.GetOrCompute("Sphere Diameter");
            control.SetLimits(1, 300); // Set the range from 1 to 300
            control.Getter = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                return gateway != null ? gateway.Settings.SphereDiameter : 50.0f;
            };
            control.Setter = (block, value) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null)
                {
                    gateway.Settings.SphereDiameter = value;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                }
            };
            control.Writer = (block, sb) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null)
                {
                    sb.Append($"{gateway.Settings.SphereDiameter} meters");
                }
            };
            control.SupportsMultipleBlocks = true;
            return control;
        }

        private static IMyTerminalAction CreateToggleShowSphereAction()
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("ToggleShowSphere");
            action.Name = new StringBuilder("Toggle Show Sphere");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds"; // You may want to use a different icon
            action.Action = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null)
                {
                    gateway.Settings.ShowSphere = !gateway.Settings.ShowSphere;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: ShowSphere toggled to {gateway.Settings.ShowSphere} for EntityId: {block.EntityId}");
                }
            };
            action.Writer = (b, sb) => sb.Append(b.GameLogic.GetAs<TeleportGateway>()?.Settings.ShowSphere == true ? "Hide Sphere" : "Show Sphere");
            return action;
        }

        private static IMyTerminalAction CreateShowSphereOnAction()
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("ShowSphereOn");
            action.Name = new StringBuilder("Show Sphere On");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds"; // You may want to use a different icon
            action.Action = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null)
                {
                    gateway.Settings.ShowSphere = true;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: ShowSphere set to true for EntityId: {block.EntityId}");
                }
            };
            action.Writer = (b, sb) => sb.Append("Show Sphere On");
            return action;
        }

        private static IMyTerminalAction CreateShowSphereOffAction()
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<IMyCollector>("ShowSphereOff");
            action.Name = new StringBuilder("Show Sphere Off");
            action.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds"; // You may want to use a different icon
            action.Action = (block) =>
            {
                var gateway = block.GameLogic.GetAs<TeleportGateway>();
                if (gateway != null)
                {
                    gateway.Settings.ShowSphere = false;
                    gateway.Settings.Changed = true;
                    gateway.TrySave();
                    MyLogger.Log($"TPGate: ShowSphere set to false for EntityId: {block.EntityId}");
                }
            };
            action.Writer = (b, sb) => sb.Append("Show Sphere Off");
            return action;
        }

        private void JumpAction(IMyCollector block)
        {
            MyLogger.Log($"TPGate: JumpAction: Jump action triggered for EntityId: {block.EntityId}");

            var link = Settings.GatewayName;
            if (string.IsNullOrEmpty(link)) return;

            // Instead of performing the teleport logic here, send a request to the server
            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                MyLogger.Log($"TPGate: JumpAction: Sending jump request to server");
                var message = new JumpRequestMessage
                {
                    GatewayId = block.EntityId,
                    Link = link
                };
                MyAPIGateway.Multiplayer.SendMessageToServer(NetworkHandler.JumpRequestId, MyAPIGateway.Utilities.SerializeToBinary(message));
            }
            else
            {
                // If we are the server, process the jump directly
                ProcessJumpRequest(block.EntityId, link);
            }
        }

        // New method to process jump requests on the server
        public static void ProcessJumpRequest(long gatewayId, string link)
        {
            MyLogger.Log($"TPGate: ProcessJumpRequest: Processing jump request for gateway {gatewayId}, link {link}");

            var block = MyAPIGateway.Entities.GetEntityById(gatewayId) as IMyCollector;
            if (block == null) return;

            // Update teleport links
            TeleportCore.UpdateTeleportLinks();

            var playerList = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(playerList);

            bool teleportAttempted = false;
            int playersToTeleport = 0;
            int shipsToTeleport = 0;

            foreach (var player in playerList)
            {
                float sphereRadius = block.GameLogic.GetAs<TeleportGateway>()?.Settings.SphereDiameter / 2.0f ?? 25.0f;
                var distance = Vector3D.Distance(player.GetPosition(), block.GetPosition() + block.WorldMatrix.Forward * sphereRadius);

                if (distance <= sphereRadius)
                {
                    TeleportCore.RequestTeleport(player.IdentityId, block.EntityId, link);
                    teleportAttempted = true;
                    playersToTeleport++;

                    if (player.Controller.ControlledEntity is IMyShipController)
                    {
                        shipsToTeleport++;
                    }
                }
            }

            var destGatewayId = TeleportCore.GetDestinationGatewayId(link, block.EntityId);
            var destGateway = MyAPIGateway.Entities.GetEntityById(destGatewayId) as IMyCollector;
            if (destGateway != null)
            {
                var unpilotedShipsCount = TeleportCore.TeleportNearbyShips(block, destGateway);
                shipsToTeleport += unpilotedShipsCount;
                if (unpilotedShipsCount > 0)
                {
                    teleportAttempted = true;
                }
            }

            if (teleportAttempted)
            {
                MyLogger.Log($"TPGate: ProcessJumpRequest: Teleport attempted");
                MyAPIGateway.Utilities.ShowNotification($"TPGate: Teleporting {playersToTeleport} player(s) and {shipsToTeleport} ship(s)", 5000, "White");
            }
        }
    }
}
