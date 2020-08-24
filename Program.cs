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
        #region mdk preserve

        const string ScriptPrefixTag = "EPOWER";

        string DebugTerminalTag = ScriptPrefixTag + ":DebugTerminal";

        string EmergencyPowerTag = ScriptPrefixTag + ":EmergencyPower";

        bool DisplayPowerCapacityOnBlockName = false;

        // The minimum current that power generators should provide before batteries get activated and discharged to provide enough current to the grid and the value is in Megawatts
        float MinimumOutputThreshold = 1.5f;
        
        // If the batteries go below this threshold reactors will be turned on in order to charge batteries and give enough power to the grid
        float CriticalBatteryCapacity = 0.2f;
        
        // The overall batteries capacity in order to consider them charged
        float ChargedBatteryCapacity = 0.8f;
        
        // whether to use real time (second between calls) or pure UpdateFrequency for update frequency
        readonly bool USE_REAL_TIME = false;

        // Defines the FREQUENCY.
        const UpdateFrequency FREQUENCY = UpdateFrequency.Update100;

        #endregion

        /// <summary>
        /// How often the script should update in milliseconds
        /// </summary>
        const int UPDATE_REAL_TIME = 1000;
        /// <summary>
        /// The maximum run time of the script per call.
        /// Measured in milliseconds.
        /// </summary>
        const double MAX_RUN_TIME = 35;
        /// <summary>
        /// The maximum percent load that this script will allow
        /// regardless of how long it has been executing.
        /// </summary> 
        const double MAX_LOAD = 0.8;

        /// <summary>
        /// A wrapper for the <see cref="Echo"/> function that adds the log to the stored log.
        /// This allows the log to be remembered and re-outputted without extra work.
        /// </summary>
        Action<string> EchoR;
        /// <summary>
        /// Stores the output of Echo so we can effectively ignore some calls
        /// without overwriting it.
        /// </summary>
        public StringBuilder EchoOutput = new StringBuilder();
        /// <summary>
        /// Defines the terminalCycle.
        /// </summary>
        IEnumerator<bool> terminalCycle;
        /// <summary>
        /// Display for debug purpose
        /// </summary>
        DebugTerminal debugTerminals;

        #region Script state & storage

        /// <summary>
        /// The time we started the last cycle at.
        /// If <see cref="USE_REAL_TIME"/> is <c>true</c>, then it is also used to track
        /// when the script should next update
        /// </summary>
        DateTime currentCycleStartTime;
        /// <summary>
        /// The time the previous step ended
        /// </summary>
        DateTime previousStepEndTime;
        /// <summary>
        /// The time to wait before starting the next cycle.
        /// Only used if <see cref="USE_REAL_TIME"/> is <c>true</c>.
        /// </summary>
        TimeSpan cycleUpdateWaitTime = new TimeSpan(0, 0, 0, 0, UPDATE_REAL_TIME);
        /// <summary>
        /// The total number of calls this script has had since compilation.
        /// </summary>
        long totalCallCount;
        /// <summary>
        /// The text to echo at the start of each call.
        /// </summary>
        string scriptUpdateText;
        /// <summary>
        /// The current step in the TIM process cycle.
        /// </summary>
        int processStep;
        /// <summary>
        /// All of the process steps that TIM will need to take,
        /// </summary>
        readonly Action[] processSteps;

        bool criticalBatteryCapacityDetected = false;

        IEnumerator<IMyPowerProducer> powerProducerCycle;
        #endregion

        #region Version

        const string SCRIPT_NAME = "ED's Emergency Power";
        // current script version
        const int VERSION_MAJOR = 1, VERSION_MINOR = 0, VERSION_REVISION = 6;
        /// <summary>
        /// Current script update time.
        /// </summary>
        const string VERSION_UPDATE = "2020-08-13";
        /// <summary>
        /// A formatted string of the script version.
        /// </summary>
        readonly string VERSION_NICE_TEXT = string.Format("v{0}.{1}.{2} ({3})", VERSION_MAJOR, VERSION_MINOR, VERSION_REVISION, VERSION_UPDATE);

        #endregion

        #region Format Strings

        /// <summary>
        /// The format for the text to echo at the start of each call.
        /// </summary>
        const string FORMAT_UPDATE_TEXT = "{0}\n{1}\nLast run: #{{0}} at {{1}}";

        #endregion

        #region Properties

        /// <summary>
        /// The length of time we have been executing for.
        /// Measured in milliseconds.
        /// </summary>
        int ExecutionTime
        {
            get { return (int)((DateTime.Now - currentCycleStartTime).TotalMilliseconds + 0.5); }
        }

        /// <summary>
        /// The current percent load of the call.
        /// </summary>
        double ExecutionLoad
        {
            get { return Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount; }
        }

        #endregion

        public Program()
        {
            // init echo wrapper
            EchoR = log =>
            {
                EchoOutput.AppendLine(log);
                Echo(log);
            };

            debugTerminals = new DebugTerminal(this);
            terminalCycle = SetTerminalCycle();

            // initialise the process steps we will need to do
            processSteps = new Action[]
            {
                ProcessStepDischargeBatteriesOnLowCurrent,
                ProcessStepCheckBatteryStatus,
                ProcessStepRechargeBatteries,
                ProcessStepUpdateBlockName,
            };

            Runtime.UpdateFrequency = FREQUENCY;

            EchoR(string.Format("Compiled {0} {1}", SCRIPT_NAME, VERSION_NICE_TEXT));

            // format terminal info text
            scriptUpdateText = string.Format(FORMAT_UPDATE_TEXT, SCRIPT_NAME, VERSION_NICE_TEXT);
        }

        public void Save()
        {
            
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (USE_REAL_TIME)
            {
                DateTime n = DateTime.Now;
                if (n - currentCycleStartTime >= cycleUpdateWaitTime)
                    currentCycleStartTime = n;
                else
                {
                    Echo(EchoOutput.ToString()); // ensure that output is not lost
                    return;
                }
            }
            else
            {
                currentCycleStartTime = DateTime.Now;
            }

            EchoOutput.Clear();
            if (processStep == processSteps.Count())
            {
                processStep = 0;
            }
            int processStepTmp = processStep;
            bool didAtLeastOneProcess = false;

            // output terminal info
            EchoR(string.Format(scriptUpdateText, ++totalCallCount, currentCycleStartTime.ToString("h:mm:ss tt")));

            try
            {
                do
                {
                    processSteps[processStep]();
                    processStep++;
                    previousStepEndTime = DateTime.Now;
                    didAtLeastOneProcess = true;
                } while (processStep < processSteps.Length && DoExecutionLimitCheck());
                // if we get here it means we completed all the process steps
                processStep = 0;
            }
            catch (PutOffExecutionException) { }
            catch (Exception ex)
            {
                // if the process step threw an exception, make sure we print the info
                // we need to debug it
                string err = "An error occured,\n" +
                    "please give the following information to the developer:\n" +
                    string.Format("Current step on error: {0}\n{1}", processStep, ex.ToString().Replace("\r", ""));
                EchoR(err);
                throw ex;
            }
            
            string stepText;
            int theoryProcessStep = processStep == 0 ? processSteps.Count() : processStep;
            int exTime = ExecutionTime;
            double exLoad = Math.Round(100.0f * ExecutionLoad, 1);
            if (processStep == 0 && processStepTmp == 0 && didAtLeastOneProcess)
                stepText = "all steps";
            else if (processStep == processStepTmp)
                stepText = string.Format("step {0} partially", processStep);
            else if (theoryProcessStep - processStepTmp == 1)
                stepText = string.Format("step {0}", processStepTmp);
            else
                stepText = string.Format("steps {0} to {1}", processStepTmp, theoryProcessStep - 1);
            EchoR(string.Format("Completed {0} in {1}ms\n{2}% load ({3} instructions)",
                stepText, exTime, exLoad, Runtime.CurrentInstructionCount));

            if (!terminalCycle.MoveNext())
            {
                terminalCycle.Dispose();
            }
        }

        void ProcessStepDischargeBatteriesOnLowCurrent()
        {
            var currentGenerators = new List<IMyPowerProducer>();
            GridTerminalSystem.GetBlocksOfType(currentGenerators, blk => CollectSameConstruct(blk) && !(blk is IMyBatteryBlock) && blk.IsWorking);
            float actualCurrentAvailable = 0; float maxCurrentOutput = 0;
            currentGenerators.ForEach(generator => {
                actualCurrentAvailable += generator.MaxOutput;
                if (generator is IMySolarPanel) {
                    maxCurrentOutput += 0.160f;
                }
                else
                {
                    maxCurrentOutput += generator.MaxOutput;
                }
            });

            EchoR(string.Format("Available: {0}MW / {1}MW", Math.Round(actualCurrentAvailable, 2), Math.Round(maxCurrentOutput, 2)));

            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk) && blk.IsFunctional && blk.Enabled);
            if (actualCurrentAvailable < MinimumOutputThreshold)
            {
                EchoR(string.Format("Low current detected: {0} MW", Math.Round(actualCurrentAvailable, 2)));
                batteries.ForEach(battery => {
                    if (battery.ChargeMode != ChargeMode.Recharge)
                    {
                        battery.ChargeMode = ChargeMode.Discharge;
                    }
                });
                EchoR("Batteries discharging");
            }

            if (actualCurrentAvailable > MinimumOutputThreshold)
            {
                batteries.ForEach(battery => {
                    if (battery.ChargeMode != ChargeMode.Recharge)
                    {
                        battery.ChargeMode = ChargeMode.Auto;
                    }
                });
            }
        }

        void ProcessStepCheckBatteryStatus()
        {
            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk) && blk.IsFunctional && blk.Enabled);
            var capacity = RemainingBatteryCapacity(batteries);
            EchoR(string.Format("Batteries capacity: {0}%", Math.Round(capacity * 100, 0)));

            var generators = new List<IMyPowerProducer>();
            GridTerminalSystem.GetBlocksOfType(generators, blk => CollectSameConstruct(blk) && MyIni.HasSection(blk.CustomData, EmergencyPowerTag));

            if (capacity < CriticalBatteryCapacity || (capacity < ChargedBatteryCapacity && criticalBatteryCapacityDetected))
            {
                criticalBatteryCapacityDetected = true;
                generators.ForEach(blk => blk.Enabled = true);
            }
            else if (criticalBatteryCapacityDetected)
            {
                criticalBatteryCapacityDetected = false;
                generators.ForEach(blk => blk.Enabled = false);
            }
        }

        void ProcessStepRechargeBatteries()
        {
            RunEveryCycles(10);
            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries, blk => CollectSameConstruct(blk) && blk.IsFunctional && blk.Enabled);
            if (batteries.Count() == 0) return;

            float remainingCapacity = RemainingBatteryCapacity(batteries);

            if (remainingCapacity < CriticalBatteryCapacity
                || (remainingCapacity < ChargedBatteryCapacity && criticalBatteryCapacityDetected))
            {
                var batteriesToCharge = Convert.ToInt16(batteries.Count / 2 + 0.5f);
                foreach (var battery in batteries.Skip(batteriesToCharge))
                {
                    battery.ChargeMode = ChargeMode.Auto;
                }
                foreach (var battery in batteries.Take(batteriesToCharge))
                {
                    battery.ChargeMode = ChargeMode.Recharge;
                }
                EchoR(string.Format("Charging batteries: {0}%", Math.Round(remainingCapacity * 100, 0)));
            }
            else
            {
                foreach (var battery in batteries)
                {
                    if (battery.ChargeMode != ChargeMode.Discharge)
                    {
                        battery.ChargeMode = ChargeMode.Auto;
                    }
                }
            }
        }

        void ProcessStepUpdateBlockName()
        {
            if (!DisplayPowerCapacityOnBlockName) return;

            if (powerProducerCycle == null)
            {
                var currentGenerators = new List<IMyPowerProducer>();
                GridTerminalSystem.GetBlocksOfType(currentGenerators, blk => CollectSameConstruct(blk) && blk.IsFunctional && blk.Enabled);
                powerProducerCycle = currentGenerators.GetEnumerator();
            }

            int cycleCount = 0;
            while (cycleCount < 5)
            {
                if (powerProducerCycle.MoveNext())
                {
                    AddPowerCapacityToGeneratorBlockName(powerProducerCycle.Current);
                }
                else
                {
                    powerProducerCycle.Dispose();
                    powerProducerCycle = null;
                    break;
                }
            }
        }

        /// <summary>
        /// The SetTerminalCycle.
        /// </summary>
        /// <returns>The <see cref="IEnumerator{bool}"/>.</returns>
        IEnumerator<bool> SetTerminalCycle()
        {
            while (true)
            {
                yield return debugTerminals.Run();
            }
        }
    }

}
