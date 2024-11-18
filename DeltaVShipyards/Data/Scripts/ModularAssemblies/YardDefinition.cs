using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;
using static Scripts.ModularAssemblies.Communication.DefinitionDefs;

namespace Scripts.ModularAssemblies
{
    /* Hey there modders!
     *
     * This file is a *template*. Make sure to keep up-to-date with the latest version, which can be found at https://github.com/StarCoreSE/Modular-Assemblies-Client-Mod-Template.
     *
     * If you're just here for the API, head on over to https://github.com/StarCoreSE/Modular-Assemblies/wiki/The-Modular-API for a (semi) comprehensive guide.
     *
     */
    internal partial class ModularDefinition
    {
        // You can declare functions in here, and they are shared between all other ModularDefinition files.
        // However, for all but the simplest of assemblies it would be wise to have a separate utilities class.

        // This is the important bit.
        internal ModularPhysicalDefinition YardDefinition => new ModularPhysicalDefinition
        {
            // Unique name of the definition.
            Name = "YardDefinition",

            OnInit = () =>
            {
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", "YardDefinition.OnInit called.");
            },

            // Triggers whenever a new part is added to an assembly.
            OnPartAdd = (assemblyId, block, isBasePart) =>
            {
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", $"YardDefinition.OnPartAdd called.\nAssembly: {assemblyId}\nBlock: {block.DisplayNameText}\nIsBasePart: {isBasePart}");
                MyAPIGateway.Utilities.ShowNotification("Assembly has " + ModularApi.GetMemberParts(assemblyId).Length + " blocks.");
            },

            // Triggers whenever a part is removed from an assembly.
            OnPartRemove = (assemblyId, block, isBasePart) =>
            {
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", $"YardDefinition.OnPartRemove called.\nAssembly: {assemblyId}\nBlock: {block.DisplayNameText}\nIsBasePart: {isBasePart}");
                MyAPIGateway.Utilities.ShowNotification("Assembly has " + ModularApi.GetMemberParts(assemblyId).Length + " blocks.");
            },

            // Triggers whenever a part is destroyed, just after OnPartRemove.
            OnPartDestroy = (assemblyId, block, isBasePart) =>
            {
                // You can remove this function, and any others if need be.
                MyAPIGateway.Utilities.ShowMessage("Modular Assemblies", $"YardDefinition.OnPartDestroy called.\nI hope the explosion was pretty.");
                MyAPIGateway.Utilities.ShowNotification("Assembly has " + ModularApi.GetMemberParts(assemblyId).Length + " blocks.");
            },

            // Optional - if this is set, an assembly will not be created until a baseblock exists.
            // 
            BaseBlockSubtype = null,

            // All SubtypeIds that can be part of this assembly.
            AllowedBlockSubtypes = new[]
            {
                "ShipyardCorner_Large",
                "ShipyardConveyor_Large",
                "ShipyardConveyorMount_Large",
            },

            // Allowed connection directions & whitelists, measured in blocks.
            // If an allowed SubtypeId is not included here, connections are allowed on all sides.
            // If the connection type whitelist is empty, all allowed subtypes may connect on that side.
            AllowedConnections = new Dictionary<string, Dictionary<Vector3I, string[]>>
            {
                ["ShipyardCorner_Large"] = new Dictionary<Vector3I, string[]>
                {
                    // In this definition, a small reactor can only connect on faces with conveyors.
                    [new Vector3I(0,2,0)] = Array.Empty<string>(), //yard corner up connection point
                    [new Vector3I(-3,-1,0)] = Array.Empty<string>(), //yard corner left connection point
                    [new Vector3I(0,-1,-3)] = Array.Empty<string>(), //yard corner forward connection point

                },
                ["ShipyardConveyor_Large"] = new Dictionary<Vector3I, string[]>
                {
                    // In this definition, a small reactor can only connect on faces with conveyors.
                    [Vector3I.Forward] = Array.Empty<string>(), // Build Info is really handy for checking directions.
                    [Vector3I.Backward] = Array.Empty<string>(),
                },
                ["ShipyardConveyorMount_Large"] = new Dictionary<Vector3I, string[]>
                {
                    // In this definition, a small reactor can only connect on faces with conveyors.
                    [Vector3I.Forward] = Array.Empty<string>(), // Build Info is really handy for checking directions.
                    [Vector3I.Backward] = Array.Empty<string>(),
                }
            },
        };
    }
}
