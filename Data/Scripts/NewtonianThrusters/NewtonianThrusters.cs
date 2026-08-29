// NewtonianThrusters.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Engine.Physics;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace NewtonianThrusters
{
    // ============================================================
    // Per-block persisted setting helper.
    // ============================================================
    public static class NewtonianSettings
    {
        // Storage GUIDs.
        private static readonly Guid Key             = new Guid("769b72d4-4655-476c-9d85-2fbc63820dcb");
        private static readonly Guid ForceWeightKey  = new Guid("7ed65f8f-fa86-4ac2-aa59-83be8e9084f2");
        private static readonly Guid LearningRateKey = new Guid("bb3e4a3e-0c0a-47a0-ab94-cc6c84b92dad");
        private static readonly Guid IterationsKey   = new Guid("80c89468-b4db-40c9-b5d1-9ceeb74f0127");
        private static readonly Guid ThrustDecayKey  = new Guid("32f81d62-de3e-4fa1-9917-a99e9a1f4c27");

        // Slider ranges + defaults.
        public const float ForceWeightMin  = 0.1f, ForceWeightMax  = 0.9f, ForceWeightDefault  = 0.3f;
        public const float LearningRateMin = 0.1f, LearningRateMax = 0.5f, LearningRateDefault = 0.1f;
        public const int   IterationsMin   = 1,    IterationsMax   = 16,   IterationsDefault   = 8;
        public const float ThrustDecayMin  = 0.0f, ThrustDecayMax  = 0.1f, ThrustDecayDefault  = 0.001f;

        // Default to ON when nothing is stored.
        public static bool GetEnabled(IMyTerminalBlock block)
        {
            if (block?.Storage == null)
                return true;

            string data;
            if (block.Storage.TryGetValue(Key, out data))
                return data != "0";
            return true;
        }

        public static void SetEnabled(IMyTerminalBlock block, bool on)
        {
            SyncSettings.Set(block, Key, on ? "1" : "0");
        }

        public static float GetForceWeight(IMyTerminalBlock block)
            => GetFloat(block, ForceWeightKey, ForceWeightDefault, ForceWeightMin, ForceWeightMax);
        public static void SetForceWeight(IMyTerminalBlock block, float value)
            => SetFloat(block, ForceWeightKey, value, ForceWeightMin, ForceWeightMax);

        public static float GetLearningRate(IMyTerminalBlock block)
            => GetFloat(block, LearningRateKey, LearningRateDefault, LearningRateMin, LearningRateMax);
        public static void SetLearningRate(IMyTerminalBlock block, float value)
            => SetFloat(block, LearningRateKey, value, LearningRateMin, LearningRateMax);

        public static int GetIterations(IMyTerminalBlock block)
            => (int)Math.Round(GetFloat(block, IterationsKey, IterationsDefault, IterationsMin, IterationsMax));
        public static void SetIterations(IMyTerminalBlock block, float value)
            => SetFloat(block, IterationsKey, (float)Math.Round(value), IterationsMin, IterationsMax);

        public static float GetThrustDecay(IMyTerminalBlock block)
            => GetFloat(block, ThrustDecayKey, ThrustDecayDefault, ThrustDecayMin, ThrustDecayMax);
        public static void SetThrustDecay(IMyTerminalBlock block, float value)
            => SetFloat(block, ThrustDecayKey, value, ThrustDecayMin, ThrustDecayMax);

        private static float GetFloat(IMyTerminalBlock block, Guid key, float def, float min, float max)
        {
            float value = def;
            if (block?.Storage != null)
            {
                string data;
                if (block.Storage.TryGetValue(key, out data))
                {
                    float parsed;
                    if (float.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                        value = parsed;
                }
            }
            return MathHelper.Clamp(value, min, max);
        }

        private static void SetFloat(IMyTerminalBlock block, Guid key, float value, float min, float max)
        {
            if (block == null)
                return;
            value = MathHelper.Clamp(value, min, max);
            SyncSettings.Set(block, key, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    // ============================================================
    // Terminal checkbox registration.
    // Deferred and retried each tick until a sample controller's
    // "DampenersOverride" property exists.
    // ============================================================
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class NewtonianSession : MySessionComponentBase
    {
        private bool _controlsRegistered;

        public override void LoadData()
        {
            if (MyAPIGateway.Multiplayer != null)
                SyncSettings.Register();
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Multiplayer != null)
                SyncSettings.Unregister();
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();

            if (_controlsRegistered)
                return;
            TryRegisterControls();
        }

        private void TryRegisterControls()
        {
            if (MyAPIGateway.TerminalControls == null)
                return;

            IMyShipController sample = FindSampleController();
            if (sample == null)
                return;

            if (sample.GetProperty("DampenersOverride") == null)
                return;

            CreateControls<IMyCockpit>();
            CreateControls<IMyRemoteControl>();
            _controlsRegistered = true;
        }

        private IMyShipController FindSampleController()
        {
            if (MyAPIGateway.Entities == null)
                return null;

            var entities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(entities, e => e is IMyCubeGrid);

            var blocks = new List<IMySlimBlock>();
            foreach (IMyEntity entity in entities)
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null)
                    continue;

                blocks.Clear();
                grid.GetBlocks(blocks, b => b.FatBlock is IMyShipController);
                foreach (IMySlimBlock block in blocks)
                {
                    var sc = block.FatBlock as IMyShipController;
                    if (sc != null)
                        return sc;
                }
            }
            return null;
        }

        private void CreateControls<TBlock>() where TBlock : IMyTerminalBlock
        {
            CreateCheckbox<TBlock>();
            CreateActions<TBlock>();

            CreateSlider<TBlock>(
                "Newtonian_ForceWeight", "Force Weight",
                NewtonianSettings.ForceWeightMin, NewtonianSettings.ForceWeightMax,
                b => NewtonianSettings.GetForceWeight(b),
                (b, v) => NewtonianSettings.SetForceWeight(b, v),
                (b, sb) => sb.Append(NewtonianSettings.GetForceWeight(b).ToString("0.00", CultureInfo.InvariantCulture)));

            CreateSlider<TBlock>(
                "Newtonian_LearningRate", "Learning Rate",
                NewtonianSettings.LearningRateMin, NewtonianSettings.LearningRateMax,
                b => NewtonianSettings.GetLearningRate(b),
                (b, v) => NewtonianSettings.SetLearningRate(b, v),
                (b, sb) => sb.Append(NewtonianSettings.GetLearningRate(b).ToString("0.00", CultureInfo.InvariantCulture)));

            CreateSlider<TBlock>(
                "Newtonian_Iterations", "Solver Iterations",
                NewtonianSettings.IterationsMin, NewtonianSettings.IterationsMax,
                b => NewtonianSettings.GetIterations(b),
                (b, v) => NewtonianSettings.SetIterations(b, v),
                (b, sb) => sb.Append(NewtonianSettings.GetIterations(b).ToString(CultureInfo.InvariantCulture)));
        
            CreateSlider<TBlock>(
                "Newtonian_ThrustDecay", "Thrust Decay",
                NewtonianSettings.ThrustDecayMin, NewtonianSettings.ThrustDecayMax,
                b => NewtonianSettings.GetThrustDecay(b),
                (b, v) => NewtonianSettings.SetThrustDecay(b, v),
                (b, sb) => sb.Append(NewtonianSettings.GetThrustDecay(b).ToString("0.00", CultureInfo.InvariantCulture)));
        }

        private void CreateCheckbox<TBlock>() where TBlock : IMyTerminalBlock
        {
            var checkbox = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlCheckbox, TBlock>("Newtonian_Balancer");
            checkbox.Title = MyStringId.GetOrCompute("Torque Balancer");
            checkbox.Tooltip = MyStringId.GetOrCompute("Throttle thrusters so net torque cancels.");
            checkbox.SupportsMultipleBlocks = true;
            checkbox.Getter = (b) => NewtonianSettings.GetEnabled(b);
            checkbox.Setter = (b, on) => NewtonianSettings.SetEnabled(b, on);
            MyAPIGateway.TerminalControls.AddControl<TBlock>(checkbox);
        }

        private void CreateSlider<TBlock>(
            string id, string title, float min, float max,
            Func<IMyTerminalBlock, float> getter,
            Action<IMyTerminalBlock, float> setter,
            Action<IMyTerminalBlock, StringBuilder> writer) where TBlock : IMyTerminalBlock
        {
            var slider = MyAPIGateway.TerminalControls
                .CreateControl<IMyTerminalControlSlider, TBlock>(id);
            slider.Title = MyStringId.GetOrCompute(title);
            slider.SupportsMultipleBlocks = true;
            slider.SetLimits(min, max);
            slider.Getter = getter;
            slider.Setter = setter;
            slider.Writer = writer;
            MyAPIGateway.TerminalControls.AddControl<TBlock>(slider);
        }

        private void CreateActions<TBlock>() where TBlock : IMyTerminalBlock
        {
            // Checkbox: toggle / on / off
            AddAction<TBlock>("Newtonian_Balancer_Toggle", "Torque Balancer - Toggle",
                b => NewtonianSettings.SetEnabled(b, !NewtonianSettings.GetEnabled(b)),
                (b, sb) => sb.Append(NewtonianSettings.GetEnabled(b) ? "On" : "Off"));

            AddAction<TBlock>("Newtonian_Balancer_On", "Torque Balancer - On",
                b => NewtonianSettings.SetEnabled(b, true),
                (b, sb) => sb.Append("On"));

            AddAction<TBlock>("Newtonian_Balancer_Off", "Torque Balancer - Off",
                b => NewtonianSettings.SetEnabled(b, false),
                (b, sb) => sb.Append("Off"));

            // Sliders: increase / decrease / reset
            AddSliderActions<TBlock>("Newtonian_ForceWeight", "Force Weight",
                b => NewtonianSettings.GetForceWeight(b),
                (b, v) => NewtonianSettings.SetForceWeight(b, v),
                NewtonianSettings.ForceWeightMin, NewtonianSettings.ForceWeightMax,
                NewtonianSettings.ForceWeightDefault,
                (b, sb) => sb.Append(NewtonianSettings.GetForceWeight(b).ToString("0.00", CultureInfo.InvariantCulture)));

            AddSliderActions<TBlock>("Newtonian_LearningRate", "Learning Rate",
                b => NewtonianSettings.GetLearningRate(b),
                (b, v) => NewtonianSettings.SetLearningRate(b, v),
                NewtonianSettings.LearningRateMin, NewtonianSettings.LearningRateMax,
                NewtonianSettings.LearningRateDefault,
                (b, sb) => sb.Append(NewtonianSettings.GetLearningRate(b).ToString("0.00", CultureInfo.InvariantCulture)));

            AddSliderActions<TBlock>("Newtonian_Iterations", "Solver Iterations",
                b => (float)NewtonianSettings.GetIterations(b),
                (b, v) => NewtonianSettings.SetIterations(b, v),
                NewtonianSettings.IterationsMin, NewtonianSettings.IterationsMax,
                NewtonianSettings.IterationsDefault,
                (b, sb) => sb.Append(NewtonianSettings.GetIterations(b).ToString(CultureInfo.InvariantCulture)));

            Action<IMyTerminalBlock, StringBuilder> decayWriter =
                (b, sb) => sb.Append(NewtonianSettings.GetThrustDecay(b).ToString("0.000", CultureInfo.InvariantCulture));

            AddAction<TBlock>("Newtonian_ThrustDecay_Increase", "Thrust Decay - Increase",
                b => NewtonianSettings.SetThrustDecay(b, MathHelper.Clamp(
                    Math.Max(NewtonianSettings.GetThrustDecay(b), NewtonianSettings.ThrustDecayMin) * 1.5f,
                    NewtonianSettings.ThrustDecayMin, NewtonianSettings.ThrustDecayMax)), decayWriter);

            AddAction<TBlock>("Newtonian_ThrustDecay_Decrease", "Thrust Decay - Decrease",
                b => NewtonianSettings.SetThrustDecay(b, MathHelper.Clamp(
                    NewtonianSettings.GetThrustDecay(b) / 1.5f,
                    NewtonianSettings.ThrustDecayMin, NewtonianSettings.ThrustDecayMax)), decayWriter);

            AddAction<TBlock>("Newtonian_ThrustDecay_Reset", "Thrust Decay - Reset",
                b => NewtonianSettings.SetThrustDecay(b, NewtonianSettings.ThrustDecayDefault), decayWriter);
        }

        private void AddAction<TBlock>(string id, string name,
            Action<IMyTerminalBlock> action,
            Action<IMyTerminalBlock, StringBuilder> writer) where TBlock : IMyTerminalBlock
        {
            var a = MyAPIGateway.TerminalControls.CreateAction<TBlock>(id);
            a.Name = new StringBuilder(name);
            a.Action = action;
            a.Writer = writer;
            a.ValidForGroups = true;
            MyAPIGateway.TerminalControls.AddAction<TBlock>(a);
        }

        private void AddSliderActions<TBlock>(string id, string name,
            Func<IMyTerminalBlock, float> getter,
            Action<IMyTerminalBlock, float> setter,
            float min, float max, float def,
            Action<IMyTerminalBlock, StringBuilder> writer) where TBlock : IMyTerminalBlock
        {
            float step = (max - min) / 10f;

            AddAction<TBlock>(id + "_Increase", name + " - Increase",
                b => setter(b, MathHelper.Clamp(getter(b) + step, min, max)), writer);

            AddAction<TBlock>(id + "_Decrease", name + " - Decrease",
                b => setter(b, MathHelper.Clamp(getter(b) - step, min, max)), writer);

            AddAction<TBlock>(id + "_Reset", name + " - Reset",
                b => setter(b, def), writer);
        }
    }

    // ============================================================
    // GRID-LEVEL CONTROLLER (one per grid)
    // ============================================================
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), false)]
    public class NewtonianBalancer : MyGameLogicComponent
    {
        // ---- Behaviour ----
        private const float CommandFraction = 0.9f;
        private const float BrakeTaperSpeed = 2.0f;   // m/s: brake fraction tapers below this relative speed
        private const float CommandEps      = 1e-4f;
        private const bool  DebugReadout    = false;

        // ---- Solver tuning (edit these) ----------------------------------
        // ForceWeight, LearningRate, SolverIterations and ThrustDecay are
        //   per-cockpit terminal sliders; defaults live in NewtonianSettings.
        private const bool  Precondition     = true;
        // ------------------------------------------------------------------

        private IMyCubeGrid _grid;
        private IMyHudNotification _dbg;

        private readonly List<IMyThrust>    _thrusters    = new List<IMyThrust>();
        private readonly List<Thruster>     _solverInput  = new List<Thruster>();
        private readonly List<IMySlimBlock> _slimScratch  = new List<IMySlimBlock>();

        // Warm-start: previous frame's normalized solution, reused next frame.
        private float[] _warmStart;

        // Edge-trigger flag: true once we've released overrides for the current
        // "balancing off" stretch. Reset whenever balancing is on, so flipping
        // off again clears exactly once.
        private bool _offCleared;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _grid = (IMyCubeGrid)Entity;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateBeforeSimulation()
        {
            if (_grid?.Physics == null || _grid.Physics.IsStatic) return;
            if (MyAPIGateway.Multiplayer != null && !MyAPIGateway.Multiplayer.IsServer) return;

            if (IsNpcOwned()) return;

            Vector3D com = _grid.Physics.CenterOfMassWorld;

            GatherThrusters(com);
            if (_thrusters.Count == 0) return;

            IMyShipController controller = TryGetController();
            bool balancingEnabled = ReadBalancingEnabled(controller);

            if (controller == null && _warmStart == null)
            {
                ApplyNetTorque(com);
                return;
            }

            if (balancingEnabled)
            {
                _offCleared = false;

                Vector3D command = Vector3D.Zero;
                float commandFraction = 0f;
                if (controller != null)
                    command = ComputeCommand(controller, out commandFraction);

                // Gravity feed-forward: overridden thrusters bypass vanilla hover.
                Vector3 gravityComp = Vector3.Zero;
                if (controller != null && controller.DampenersOverride)
                {
                    Vector3D g = controller.GetTotalGravity();                 // m/s^2, world
                    float mass = controller.CalculateShipMass().PhysicalMass;  // kg
                    gravityComp = (Vector3)(-g * mass);                        // N, world
                }

                Vector3 brakeForce = Vector3.Zero;
                if (command.LengthSquared() >= CommandEps * CommandEps)
                {
                    double total = 0;
                    Vector3 dir = Vector3.Normalize((Vector3)command);
                    for (int i = 0; i < _solverInput.Count; i++)
                    {
                        float proj = Vector3.Dot(_solverInput[i].Direction, dir);
                        if (proj > 0) total += _solverInput[i].MaxThrust * proj;  // forward-facing only
                    }

                    total = Math.Max(total, gravityComp.Length());

                    Vector3 cmd = (Vector3)command;
                    float clen = cmd.Length();
                    if (clen > 1e-6f)
                        brakeForce = cmd * (float)(commandFraction * total / clen);
                }

                Vector3 targetForce = gravityComp + brakeForce;

                if (targetForce.LengthSquared() < CommandEps * CommandEps)
                {
                    if (controller != null)
                    {
                        for (int i = 0; i < _thrusters.Count; i++)
                            _thrusters[i].Enabled = false;

                        _warmStart = null;
                    }
                }
                else if (controller != null)
                {
                    // Per-cockpit solver tuning.
                    float forceWeight  = NewtonianSettings.GetForceWeight(controller);
                    float learningRate = NewtonianSettings.GetLearningRate(controller);
                    int   iterations   = NewtonianSettings.GetIterations(controller);
                    float decay        = NewtonianSettings.GetThrustDecay(controller);

                    float[] u = ThrustSolver.Solve(
                        _solverInput,
                        targetForce,
                        Vector3.Zero,          // target torque: cancel net spin
                        forceWeight,
                        learningRate,
                        decay,
                        iterations,
                        Precondition,
                        _warmStart);           // solver ignores it if length != count

                    // u is normalized [0,1]; scale back to newtons.
                    for (int i = 0; i < _thrusters.Count; i++)
                    {
                        float thrust = u[i] * _solverInput[i].MaxThrust;
                        if (thrust > 1f)
                        {
                            _thrusters[i].Enabled = true;
                            _thrusters[i].ThrustOverride = thrust;
                        }
                        else
                        {
                            _thrusters[i].Enabled = false;
                        }
                    }

                    _warmStart = u;
                }

                ShowDebug(command);
            }
            else
            {
                // Balancing is switched off via the cockpit checkbox.
                // Release the overrides ONCE, on the transition, then leave the
                // thrusters entirely alone so vanilla control resumes.
                if (!_offCleared)
                {
                    for (int i = 0; i < _thrusters.Count; i++)
                    {
                        _thrusters[i].Enabled = true;    // hand thrusters back to vanilla
                        _thrusters[i].ThrustOverride = 0f;
                    }

                    _warmStart = null;
                    _offCleared = true;
                }

                ShowDebug(Vector3D.Zero);
            }

            ApplyNetTorque(com);
        }

        private bool ReadBalancingEnabled(IMyShipController controller)
        {
             // Controller could be null for a few frames just after game loads
            if (controller == null)
                return true;
            return NewtonianSettings.GetEnabled(controller);
        }

        private bool IsNpcOwned()
        {
            var owners = _grid.BigOwners;
            if (owners == null || owners.Count == 0)
                return false;                       // unowned -> keep running (player-side)

            long majorOwner = owners[0];
            if (majorOwner == 0)
                return false;

            var factions = MyAPIGateway.Session?.Factions;
            if (factions == null)
                return false;

            IMyFaction faction = factions.TryGetPlayerFaction(majorOwner);
            return faction != null && (faction.IsEveryoneNpc() || !faction.AcceptHumans);
        }

        private void GatherThrusters(Vector3D com)
        {
            _thrusters.Clear();
            _solverInput.Clear();

            _slimScratch.Clear();
            _grid.GetBlocks(_slimScratch);
            for (int i = 0; i < _slimScratch.Count; i++)
            {
                IMyThrust t = _slimScratch[i].FatBlock as IMyThrust;
                if (t == null || !t.IsFunctional) continue;

                _thrusters.Add(t);
                _solverInput.Add(new Thruster
                {
                    Position  = (Vector3)(t.GetPosition() - com),
                    Direction = (Vector3)ThrustWorldDir(t),
                    MaxThrust = t.MaxEffectiveThrust,
                });
            }
        }

        private Vector3D ComputeCommand(IMyShipController sc, out float commandFraction)
        {
            var matchTarget = ((Sandbox.Game.Entities.IMyControllableEntity)sc).RelativeDampeningEntity;
            Vector3D targetVelocity = (matchTarget?.Physics != null)
                ? (Vector3D)matchTarget.Physics.LinearVelocity
                : Vector3D.Zero;
            Vector3D relativeVelocity = _grid.Physics.LinearVelocity - targetVelocity;

            if (sc.MoveIndicator.LengthSquared() > 1e-6f)
            {
                // Pilot is commanding. Use full authority on the commanded direction,
                // but keep dampeners fighting drift on the perpendicular axes.
                commandFraction = CommandFraction;
                Vector3D cmdWorld = Vector3D.TransformNormal((Vector3D)sc.MoveIndicator, sc.WorldMatrix);

                if (sc.DampenersOverride && relativeVelocity.LengthSquared() > 1e-6)
                {
                    // Remove the component of relative velocity that lies along the
                    // commanded direction so the pilot retains full authority there,
                    // then add back the perpendicular remainder as a braking term.
                    Vector3D cmdDir = Vector3D.Normalize(cmdWorld);
                    Vector3D velParallel = Vector3D.Dot(relativeVelocity, cmdDir) * cmdDir;
                    Vector3D velPerp     = relativeVelocity - velParallel;

                    double scale = Math.Min(velPerp.Length() / BrakeTaperSpeed, 1.0);
                    return cmdWorld - velPerp * (CommandFraction * scale);
                }

                return cmdWorld;
            }

            // No pilot input: if dampeners are on, brake toward the relative target.
            if (sc.DampenersOverride)
            {
                double scale = Math.Min(relativeVelocity.Length() / BrakeTaperSpeed, 1.0);
                commandFraction = (float)(CommandFraction * scale);

                return -relativeVelocity;   // direction only; allocator normalizes
            }

            commandFraction = 0f;
            return Vector3D.Zero;
        }

        private void ApplyNetTorque(Vector3D com)
        {
            Vector3D netTorque = Vector3D.Zero;
            for (int i = 0; i < _thrusters.Count; i++)
            {
                IMyThrust t = _thrusters[i];
                Vector3D force = ThrustWorldDir(t) * t.CurrentThrust;
                Vector3D arm   = t.GetPosition() - com;
                netTorque += Vector3D.Cross(arm, force);
            }
            if (netTorque.LengthSquared() < 1e-6) return;

            MatrixD invRot = MatrixD.Transpose(_grid.WorldMatrix);
            Vector3 bodyTorque = (Vector3)Vector3D.TransformNormal(netTorque, invRot);
            _grid.Physics.AddForce(MyPhysicsForceType.ADD_BODY_FORCE_AND_BODY_TORQUE, null, null, bodyTorque);
        }

        private static Vector3D ThrustWorldDir(IMyThrust t) => t.WorldMatrix.Backward;

        private IMyShipController TryGetController()
        {
            _slimScratch.Clear();
            _grid.GetBlocks(_slimScratch);

            IMyShipController active     = null;
            IMyShipController activeMain = null;
            IMyShipController any        = null;

            for (int i = 0; i < _slimScratch.Count; i++)
            {
                IMyShipController sc = _slimScratch[i].FatBlock as IMyShipController;
                if (sc == null || !sc.IsWorking || !sc.ControlThrusters) continue;

                any = sc;

                if (sc.IsUnderControl)
                {
                    if (sc.IsMainCockpit) activeMain = sc;
                    else if (active == null) active = sc;
                }
            }

            if (activeMain != null) return activeMain;
            if (active     != null) return active;
    
            // No one flying. If there's exactly one controller, use its settings.
            // If there are multiple, require a main cockpit to be designated.
            int count = 0;
            IMyShipController sole = null;
            _slimScratch.Clear();
            _grid.GetBlocks(_slimScratch);
            for (int i = 0; i < _slimScratch.Count; i++)
            {
                IMyShipController sc = _slimScratch[i].FatBlock as IMyShipController;
                if (sc == null || !sc.IsWorking || !sc.ControlThrusters) continue;
                if (sc.IsMainCockpit) return sc;
                sole = sc;
                count++;
            }
    
            return count == 1 ? sole : null;
        }

        private void ShowDebug(Vector3D command)
        {
            if (!DebugReadout) return;
            if (_dbg == null) _dbg = MyAPIGateway.Utilities.CreateNotification("");
            double angVel = _grid.Physics.AngularVelocity.Length();
            _dbg.Text = $"[NT] thrusters={_thrusters.Count} cmd={command.Length():0.00} angVel={angVel:0.000}";
            _dbg.Show();
        }
    }

    // ============================================================
    // THRUST SOLVER  (pure, no side effects, fully reusable)
    // ------------------------------------------------------------
    // Projected gradient descent on thrust allocation.
    // Each thruster i produces:
    //     force  = u_i * MaxThrust_i * Direction_i
    //     torque = u_i * MaxThrust_i * (Position_i x Direction_i)
    // with u_i in [0,1]. Minimizes:
    //     loss = f*|F - targetForce|^2 + (1-f)*|T - targetTorque|^2 + lambda*sum(u^2)
    // Returns a float[] of normalized scalars u_i in [0,1].
    // ============================================================
    public struct Thruster
    {
        public Vector3 Position;    // lever arm r_i, relative to centre of mass
        public Vector3 Direction;   // unit push direction (world frame)
        public float   MaxThrust;   // max thrust magnitude
    }

    public static class ThrustSolver
    {
        public static float[] Solve(
            IReadOnlyList<Thruster> thrusters,
            Vector3 targetForce,
            Vector3 targetTorque,
            float forceWeight,        // f in [0,1]
            float learningRate,
            float lambda,
            int iterations,
            bool precondition = true,
            float[] initialU = null)  // warm start; ignored if length != count
        {
            int n = thrusters.Count;
            var result = new float[n];
            if (n == 0) return result;

            double f   = forceWeight;
            double lr  = learningRate;
            double lam = lambda;

            // Precompute, per thruster, the scaled direction sd = m*d and the scaled
            // lever sc = m*(r x d). Force/torque are then linear in U: F=sum(u*sd), T=sum(u*sc).
            var sdx = new double[n]; var sdy = new double[n]; var sdz = new double[n];
            var scx = new double[n]; var scy = new double[n]; var scz = new double[n];
            var curv = new double[n];
            var pen  = new double[n];

            for (int i = 0; i < n; i++)
            {
                Thruster t = thrusters[i];
                double m = t.MaxThrust;
                double dx = t.Direction.X, dy = t.Direction.Y, dz = t.Direction.Z;
                double rx = t.Position.X,  ry = t.Position.Y,  rz = t.Position.Z;

                sdx[i] = m * dx; sdy[i] = m * dy; sdz[i] = m * dz;

                double cx = ry * dz - rz * dy;   // r x d
                double cy = rz * dx - rx * dz;
                double cz = rx * dy - ry * dx;
                scx[i] = m * cx; scy[i] = m * cy; scz[i] = m * cz;

                double sdLen2 = sdx[i]*sdx[i] + sdy[i]*sdy[i] + sdz[i]*sdz[i];   // = m^2
                double scLen2 = scx[i]*scx[i] + scy[i]*scy[i] + scz[i]*scz[i];   // = m^2 |r x d|^2
                double physCurv = f * sdLen2 + (1.0 - f) * scLen2;
                pen[i]  = lam * physCurv;
                curv[i] = 2.0 * (physCurv + pen[i]) + 1e-6;
            }

            // Working copy -- does not touch initialU.
            var u = new double[n];
            bool warm = (initialU != null && initialU.Length == n);
            for (int i = 0; i < n; i++) u[i] = warm ? initialU[i] : 0.0;

            double ftx = targetForce.X,  fty = targetForce.Y,  ftz = targetForce.Z;
            double ttx = targetTorque.X, tty = targetTorque.Y, ttz = targetTorque.Z;

            for (int it = 0; it < iterations; it++)
            {
                double Fx = 0, Fy = 0, Fz = 0, Tx = 0, Ty = 0, Tz = 0;
                for (int i = 0; i < n; i++)
                {
                    double ui = u[i];
                    Fx += ui * sdx[i]; Fy += ui * sdy[i]; Fz += ui * sdz[i];
                    Tx += ui * scx[i]; Ty += ui * scy[i]; Tz += ui * scz[i];
                }

                double gFx = Fx - ftx, gFy = Fy - fty, gFz = Fz - ftz;
                double gTx = Tx - ttx, gTy = Ty - tty, gTz = Tz - ttz;

                for (int i = 0; i < n; i++)
                {
                    double gradF = gFx * sdx[i] + gFy * sdy[i] + gFz * sdz[i];   // (F-Ft) . (m d)
                    double gradT = gTx * scx[i] + gTy * scy[i] + gTz * scz[i];   // (T-Tt) . (m r x d)
                    double grad  = 2.0 * f * gradF + 2.0 * (1.0 - f) * gradT + 2.0 * pen[i] * u[i];
                    double step  = precondition ? lr * grad / curv[i] : lr * grad;

                    double nu = u[i] - step;
                    u[i] = nu < 0.0 ? 0.0 : (nu > 1.0 ? 1.0 : nu);
                }

                const double MaxULenSq = 1e6;
                double lenSq = 0.0;
                for (int i = 0; i < n; i++) lenSq += u[i] * u[i];
                if (!(lenSq <= MaxULenSq))
                {
                    for (int i = 0; i < n; i++) u[i] = 0.0;
                    break;
                }
            }

            for (int i = 0; i < n; i++) result[i] = (float)u[i];
            return result;
        }
    }
    public static class SyncSettings
    {
        private const ushort ChannelId = 19281;

        public static void Register()
            => MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ChannelId, OnPacketReceived);

        public static void Unregister()
            => MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelId, OnPacketReceived);

        // Call this instead of writing block.Storage directly.
        public static void Set(IMyTerminalBlock block, Guid key, string value)
        {
            WriteLocal(block, key, value);

            if (MyAPIGateway.Multiplayer == null || MyAPIGateway.Multiplayer.IsServer)
                return; // singleplayer or listen-server: local write is enough

            // Client: send to server.
            byte[] packet = Encode(block.EntityId, key, value);
            MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, packet);
        }

        private static void OnPacketReceived(ushort channel, byte[] data, ulong sender, bool isServer)
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;

            long entityId; Guid key; string value;
            if (!Decode(data, out entityId, out key, out value)) return;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(entityId, out entity)) return;

            var block = entity as IMyTerminalBlock;
            if (block == null) return;

            WriteLocal(block, key, value);
        }

        private static void WriteLocal(IMyTerminalBlock block, Guid key, string value)
        {
            if (block.Storage == null)
                block.Storage = new MyModStorageComponent();
            block.Storage[key] = value;
        }

        private static byte[] Encode(long entityId, Guid key, string value)
        {
            byte[] idBytes  = BitConverter.GetBytes(entityId);
            byte[] keyBytes = key.ToByteArray();               // always 16 bytes
            byte[] valBytes = System.Text.Encoding.UTF8.GetBytes(value);
            byte[] lenBytes = BitConverter.GetBytes((ushort)valBytes.Length);

            var buf = new byte[8 + 16 + 2 + valBytes.Length];
            int pos = 0;
            Buffer.BlockCopy(idBytes,  0, buf, pos, 8);  pos += 8;
            Buffer.BlockCopy(keyBytes, 0, buf, pos, 16); pos += 16;
            Buffer.BlockCopy(lenBytes, 0, buf, pos, 2);  pos += 2;
            Buffer.BlockCopy(valBytes, 0, buf, pos, valBytes.Length);
            return buf;
        }

        private static bool Decode(byte[] buf, out long entityId, out Guid key, out string value)
        {
            entityId = 0; key = Guid.Empty; value = null;
            if (buf == null || buf.Length < 27) return false; // 8+16+2+1 minimum

            int pos = 0;
            entityId = BitConverter.ToInt64(buf, pos); pos += 8;

            byte[] keyBytes = new byte[16];
            Buffer.BlockCopy(buf, pos, keyBytes, 0, 16); pos += 16;
            key = new Guid(keyBytes);

            ushort len = BitConverter.ToUInt16(buf, pos); pos += 2;
            if (pos + len > buf.Length) return false;
            value = System.Text.Encoding.UTF8.GetString(buf, pos, len);
            return true;
        }
    }
}