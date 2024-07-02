﻿using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI.Weapons;
using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game;

using Sandbox.ModAPI;
using VRageMath;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using Sandbox.Definitions;
//using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using ParallelTasks;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using Sandbox.Game.Lights;

using System.Threading;
using System.Text;
using VRage.Utils;
using VRage.Library.Utils;
using Sandbox.Game.SessionComponents;
using Sandbox.Graphics;
using VRage;
using Sandbox.Game.Entities.Cube;
using VRage.Game.Entity;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace bobzone
{

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.AfterSimulation)]
    public class Session : MySessionComponentBase
    {
        public static bool isHost;
        public static bool isServer;
        public static double tock;
        public static Session Instance;



        public static float radius = 500f;
        public long tick = 0;
        private HashSet<IMyPlayer> players;
        private List<byte> spacket = new List<byte>();
        private List<IMyCubeGrid> dirties = new List<IMyCubeGrid>();
        public static HashSet<IMyCubeBlock> zoneblocks = new HashSet<IMyCubeBlock>();
        //public static ConcurrentBag<IMyCubeGrid> dirties = new ConcurrentBag<IMyCubeGrid>();
        public static Dictionary<long, long> zonelookup = new Dictionary<long, long>();

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(9, ProcessDamage);
            
        }
      
        public override void BeforeStart()
        {
            isHost = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer;
            isServer = MyAPIGateway.Utilities.IsDedicated;
        }
        
        public override void UpdateBeforeSimulation()
        {
            if (MyAPIGateway.Session == null)
            {
                return;
            }

            tick++;

            //MyAPIGateway.Utilities.ShowNotification("PLAYER: " + MyAPIGateway.Session.Player.Identity.IdentityId.ToString(), 1);

            //if (isHost)
            //{
            List<IMyPlayer> playerlist = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(playerlist);
            players = new HashSet<IMyPlayer>(playerlist);
            MyAPIGateway.Parallel.ForEach(players, x =>
            {
                var player = x as IMyPlayer;
                if (player == null)
                    return;

                foreach (IMyCubeBlock zoneblock in zoneblocks)
                {
                    if (player.Controller?.ControlledEntity?.Entity is IMyCharacter)
                    {
                        IMyCharacter character = player.Controller.ControlledEntity.Entity as IMyCharacter;
                        double distance = (zoneblock.WorldMatrix.Translation - character.WorldMatrix.Translation).LengthSquared();
                        if (distance < radius*radius)
                        {
                            if (MyIDModule.GetRelationPlayerBlock(zoneblock.OwnerId, player.Identity.IdentityId) == MyRelationsBetweenPlayerAndBlock.Enemies)
                            {
                                character.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, Vector3D.Normalize(character.WorldMatrix.Translation - zoneblock.WorldMatrix.Translation) * 1000 * character.Physics.Mass, null, null, applyImmediately: true);
                            }
                            MyVisualScriptLogicProvider.SetPlayersHydrogenLevel(player.Identity.IdentityId, 1f);

                        }
                    }
                    else if (player.Controller?.ControlledEntity?.Entity is IMyCubeBlock && MyIDModule.GetRelationPlayerBlock(zoneblock.OwnerId, player.Identity.IdentityId) == MyRelationsBetweenPlayerAndBlock.Enemies)
                    {
                        IMyCubeBlock terminalBlock = player.Controller.ControlledEntity.Entity as IMyCubeBlock;
                        IMyCubeGrid grid = terminalBlock.CubeGrid;
                        var sphere = new BoundingSphereD(zoneblock.WorldMatrix.Translation, radius);
                        if (grid.WorldAABB.Intersects(ref sphere))
                        {
                            grid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, Vector3D.Normalize(grid.Physics.CenterOfMassWorld - zoneblock.WorldMatrix.Translation) * 1000 * grid.Physics.Mass, null, null, applyImmediately: true);
                        }
                    }

                }
            });
            //}
        }

    public void ProcessDamage(object target, ref MyDamageInformation info)
        {
            IMySlimBlock slim = target as IMySlimBlock;
            long idZone = 0;

            if (slim == null || slim.CubeGrid == null)
                return;

            Vector3D targetPosition = Vector3D.Zero;
            slim.ComputeWorldCenter(out targetPosition);

            foreach (IMyCubeBlock zoneblock in zoneblocks)
            {
                double distance = (zoneblock.WorldMatrix.Translation - targetPosition).LengthSquared();
                if (distance < radius * radius)
                {
                    idZone = zoneblock.OwnerId;
                    break;
                }
            }

            if (idZone == 0)
                return;

            if (info.Type == MyDamageType.Grind)
            {
                IMyEntity ent = MyAPIGateway.Entities.GetEntityById(info.AttackerId);
                IMyPlayer player = MyAPIGateway.Players.GetPlayerControllingEntity(ent);

                if (player != null && MyIDModule.GetRelationPlayerBlock(idZone, player.Identity.IdentityId) == MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    info.Amount = 0;
                   // MyAPIGateway.Utilities.ShowNotification("IS ENEMY. OWNER = " + idZone.ToString() + ", ATTACKER = " + player.Identity.IdentityId.ToString(), 600);
                }
                else
                {
                    IMyHandheldGunObject<MyToolBase> tool = ent as IMyAngleGrinder;
                    IMyCubeBlock block = ent as IMyCubeBlock;
                    if (block != null)
                    {
                        if (MyIDModule.GetRelationPlayerBlock(idZone, block.OwnerId) == MyRelationsBetweenPlayerAndBlock.Enemies)
                        {
                            info.Amount = 0;
                            //MyAPIGateway.Utilities.ShowNotification("IS ENEMY GRINDER BLOCK. OWNER = " + idZone.ToString() + ", ATTACKER = " + block.OwnerId.ToString(), 600);
                        }
                        //else
                            //MyAPIGateway.Utilities.ShowNotification("IS FRIENDLY GRINDER BLOCK", 600);
                    }
                    else if (tool != null)
                    {
                        if (MyIDModule.GetRelationPlayerBlock(idZone, tool.OwnerIdentityId) == MyRelationsBetweenPlayerAndBlock.Enemies)
                        {
                            info.Amount = 0;
                            //MyAPIGateway.Utilities.ShowNotification("IS ENEMY GRINDER TOOL. OWNER = " + idZone.ToString() + ", ATTACKER = " + tool.OwnerId.ToString(), 600);
                        }
                        //else
                            //MyAPIGateway.Utilities.ShowNotification("IS FRIENDLY GRINDER TOOl", 600);
                    }
                    //else
                        //MyAPIGateway.Utilities.ShowNotification("IS FRIENDLY", 600);
                }
            }
            else
            {
                info.Amount = 0;
               // MyAPIGateway.Utilities.ShowNotification("IS WEAPON", 600);
            }

        }

    }
    
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), false, "bobzone")]
    public class bobzoneblock : MyGameLogicComponent
    {

        public IMyUpgradeModule ModBlock { get; private set; }
        public IMyCubeGrid ModGrid;
        public string faction;
        public float radius = 50f; // defer to session value
        public Color color = new Color(255, 255, 255, 10);
        private MatrixD matrix;
        private long tock = 0;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
            ModBlock = Entity as IMyUpgradeModule;
            ModGrid = ModBlock.CubeGrid;
            radius = Session.radius;
        }

        public override void UpdateAfterSimulation()
        {
            if (ModBlock == null || !ModBlock.IsFunctional || ModBlock.MarkedForClose || ModBlock.Closed || ModBlock.MarkedForClose || !ModBlock.Enabled || !ModGrid.IsStatic)
            {

                lock (Session.zoneblocks)
                {
                    Session.zoneblocks.Remove(ModBlock);
                }
                
                return;
            }

            //MyAPIGateway.Utilities.ShowNotification("ZONEBLOCK: " + ModBlock.OwnerId.ToString(), 1);

            lock (Session.zoneblocks)
            {
                Session.zoneblocks.Add(ModBlock);
            }
            
            tock++;
            Vector3D storage = matrix.Up;
            matrix = ModBlock.WorldMatrix;
            double rad = ((double)tock/100) % 360 * Math.PI / 180;
            matrix = MatrixD.CreateWorld(matrix.Translation, matrix.Up, matrix.Right * Math.Cos(rad) + matrix.Forward * Math.Sin(rad));          
            if (!Session.isServer)
            {

                double renderdistance = (matrix.Translation - MyAPIGateway.Session.Camera.Position).Length();

                if(renderdistance < 20*radius)
                    MySimpleObjectDraw.DrawTransparentSphere(ref matrix, radius, ref color, MySimpleObjectRasterizer.Solid, 20, MyStringId.GetOrCompute("SafeZoneShield_Material"), null, -1, -1, null, BlendTypeEnum.PostPP, 1);
                    //MySimpleObjectDraw.DrawTransparentSphere(ref matrix, radius.Value, ref drawColor, MySimpleObjectRasterizer.Solid, 35, shield_mat, null, -1, -1, null, BlendTypeEnum.PostPP, 1);

                //if (renderdistance < radius)
                    //MyAPIGateway.Utilities.ShowNotification("INSIDE " + ModBlock.GetOwnerFactionTag() + "'S BOBZONE", 1);

            }
        }
    }
}