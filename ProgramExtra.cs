using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{

    partial class Program : MyGridProgram
    {
        void RetrieveCustomSetting()
        {
            // init settings
            _ini.TryParse(Me.CustomData);

            MinimumOutputThreshold = _ini.Get(ScriptPrefixTag, "MinimumOutputThreshold").ToSingle(1.5f);
            CriticalBatteryCapacity = _ini.Get(ScriptPrefixTag, "CriticalBatteryCapacity").ToSingle(0.2f);
            ChargedBatteryCapacity = _ini.Get(ScriptPrefixTag, "ChargedBatteryCapacity").ToSingle(0.8f);
            DisplayPowerCapacityOnBlockName = _ini.Get(ScriptPrefixTag, "DisplayPowerCapacity").ToBoolean(false);
        }

        static float RemainingBatteryCapacity(List<IMyBatteryBlock> batteries)
        {
            float totalStoredPower = 0; float totalMaxStoredPower = 0;
            foreach (var battery in batteries)
            {
                totalStoredPower += battery.CurrentStoredPower;
                totalMaxStoredPower += battery.MaxStoredPower;
            }

            return totalStoredPower / totalMaxStoredPower;
        }

        /// <summary>
        /// Checks if the terminal is null, gone from world, or broken off from grid.
        /// </summary>
        /// <param name="block">The block<see cref="T"/>.</param>
        /// <returns>The <see cref="bool"/>.</returns>
        bool IsCorrupt(IMyTerminalBlock block)
        {
            bool isCorrupt = block == null || block.WorldMatrix == MatrixD.Identity
                || !(GridTerminalSystem.GetBlockWithId(block.EntityId) == block);

            return isCorrupt;
        }

        /// <summary>
        /// Checks if the current call has exceeded the maximum execution limit.
        /// If it has, then it will raise a <see cref="PutOffExecutionException:T"/>.
        /// </summary>
        /// <returns>True.</returns>
        /// <remarks>This methods returns true by default to allow use in the while check.</remarks>
        bool DoExecutionLimitCheck()
        {
            if (ExecutionTime > MAX_RUN_TIME || ExecutionLoad > MAX_LOAD)
                throw new PutOffExecutionException();
            return true;
        }
        
        bool CollectSameConstruct(IMyTerminalBlock block)
        {
            return block.IsSameConstructAs(Me);
        }

        void RunEveryCycles(int cycles)
        {
            if (DateTime.Now - previousStepEndTime > TimeSpan.FromMilliseconds(100) && totalCallCount % cycles != 0)
            {
                throw new PutOffExecutionException();
            }
        }

        void AddPowerCapacityToGeneratorBlockName(IMyPowerProducer generator)
        {
            float availablePower = 100f;
            if (generator is IMyBatteryBlock)
            {
                var storedPower = (generator as IMyBatteryBlock).CurrentStoredPower;
                var maxPower = (generator as IMyBatteryBlock).MaxStoredPower;
                availablePower = storedPower / maxPower * 100;
            }
            else if (generator is IMySolarPanel)
            {
                availablePower = (generator as IMySolarPanel).MaxOutput / 0.160f * 100;
            }
            if (!generator.IsWorking) availablePower = 0;

            var idx = generator.CustomName.Trim().LastIndexOf('[');
            if (idx == -1) idx = generator.CustomName.Count();
            generator.CustomName = string.Format("{0} [{1}%]", generator.CustomName.Substring(0, idx).Trim(), Math.Round(availablePower).ToString());
        }

        int SortByStoredPower(IMyBatteryBlock b1, IMyBatteryBlock b2)
        {
            return b1.CurrentStoredPower.CompareTo(b2.CurrentStoredPower);
        }

        /// <summary>
        /// Thrown when we detect that we have taken up too much processing time
        /// and need to put off the rest of the exection until the next call.
        /// </summary>
        class PutOffExecutionException : Exception { }
    }
}
