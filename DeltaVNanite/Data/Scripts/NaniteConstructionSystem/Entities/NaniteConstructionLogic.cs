using System;
using Sandbox.Common.ObjectBuilders;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRage.Game.ModAPI;
using VRage.Utils;
using Sandbox.Game.Entities;

namespace NaniteConstructionSystem.Entities
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), true, "LargeNaniteControlFacility", "SmallNaniteControlFacility")]
    public class LargeControlFacilityLogic : MyGameLogicComponent
    {
        private NaniteConstructionBlock m_block = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try {
                base.UpdateOnceBeforeFrame();

                m_block = new NaniteConstructionBlock(Entity);

                if (!NaniteConstructionManager.NaniteBlocks.ContainsKey(Entity.EntityId))
                    NaniteConstructionManager.NaniteBlocks.Add(Entity.EntityId, m_block);

                m_block.UpdateCount += NaniteConstructionManager.NaniteBlocks.Count * 30;
                // Adds some gap between factory processing so they don't all process their targets at once.

                IMySlimBlock slimBlock = ((MyCubeBlock)m_block.ConstructionBlock).SlimBlock as IMySlimBlock;
                Logging.Instance.WriteLine(string.Format("ADDING Nanite Factory: conid={0} physics={1} ratio={2}",
                    Entity.EntityId, m_block.ConstructionBlock.CubeGrid.Physics == null, slimBlock.BuildLevelRatio), 1);

                if (NaniteConstructionManager.NaniteSync != null)
                    NaniteConstructionManager.NaniteSync.SendNeedTerminalSettings(Entity.EntityId);

            } catch(Exception exc) {
                MyLog.Default.WriteLine($"##MOD: Nanites UpdateOnceBeforeFrame, ERROR: {exc}");
            }
        }

        public override void UpdateBeforeSimulation()
        {
            try {
                m_block.Update();
            } catch (Exception e) {
                Logging.Instance.WriteLine($"LargeControlFacilityLogic.UpdateBeforeSimulation Exception: {e}");
            }
        }

        public override void Close()
        {
            if (NaniteConstructionManager.NaniteBlocks != null && Entity != null)
            {
                NaniteConstructionManager.NaniteBlocks.Remove(Entity.EntityId);
                Logging.Instance.WriteLine($"REMOVING Nanite Factory: {Entity.EntityId}", 1);
            }

            m_block?.Unload();
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Projector), true)]
    public class NaniteProjectorLogic : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            try
            {
                // NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                // NeedsUpdate |= VRage.ModAPI.MyEntityUpdateEnum.EACH_FRAME;

                if (Entity == null) return;

                if (!NaniteConstructionManager.ProjectorBlocks.ContainsKey(Entity.EntityId))
                    NaniteConstructionManager.ProjectorBlocks.Add(Entity.EntityId, (IMyCubeBlock)Entity);
                
            }
            catch (Exception e)
            {
                Logging.Instance.WriteLine($"NaniteProjectorLogic.Init Exception: {e}");
            }
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }

        public override void Close()
        {
            if (Entity == null) return;

            if (NaniteConstructionManager.ProjectorBlocks.ContainsKey(Entity.EntityId))
                NaniteConstructionManager.ProjectorBlocks.Remove(Entity.EntityId);
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Assembler), true)]
    public class NaniteAssemblerLogic : MyGameLogicComponent
    {
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            try {
                if (Entity == null) return;

                var cubeBlock = (IMyCubeBlock)Entity;

                if (cubeBlock == null) return;

                if (NaniteConstructionManager.AssemblerBlocks == null) return;
                
                if (!NaniteConstructionManager.AssemblerBlocks.ContainsKey(Entity.EntityId))
                {
                    NaniteConstructionManager.AssemblerBlocks.Add(Entity.EntityId, cubeBlock);
                    if (NaniteConstructionManager.NaniteSync != null)
                        NaniteConstructionManager.NaniteSync.SendNeedAssemblerSettings(Entity.EntityId);
                }
            } catch (Exception e) {
                Logging.Instance.WriteLine($"NaniteAssemblerLogic.Init Exception: {e}");
            }
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }

        public override void Close()
        {
            if (Entity == null) return;

            if (NaniteConstructionManager.AssemblerBlocks.ContainsKey(Entity.EntityId))
                NaniteConstructionManager.AssemblerBlocks.Remove(Entity.EntityId);
        }
    }
}
