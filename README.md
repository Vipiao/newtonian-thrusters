# Newtonian Thrusters

A Space Engineers mod that makes thrusters apply torque about the centre of mass, and solves the
thrust allocation problem that this creates.

## Background

In vanilla Space Engineers a thruster contributes force to the grid but no torque, regardless of
where it is mounted. A ship with all its thrust on one side accelerates in a straight line. This is
convenient, and it means thruster placement carries almost no design cost.

This mod removes that simplification. Each thruster now produces

    force  = u_i * MaxThrust_i * Direction_i
    torque = u_i * MaxThrust_i * (Position_i x Direction_i)

with the lever arm taken relative to the current centre of mass. Off-axis thrust now spins the
grid, and a ship has to be either balanced or actively stabilised.

Making that playable is the actual work. Once thrust and rotation are coupled, deciding how hard to
run each thruster stops being independent per axis and becomes an allocation problem.

## The solver

Given a commanded force and torque, find per-thruster throttles `u` in `[0,1]` minimising

    loss = f * |F - F_target|^2  +  (1 - f) * |T - T_target|^2  +  lambda * sum(u^2)

where `F = sum(u_i * m_i * d_i)` and `T = sum(u_i * m_i * (r_i x d_i))` are both linear in `u`.

The problem is a bound-constrained weighted least squares. It is solved with projected gradient
descent:

- `f` trades force tracking against torque tracking, exposed as a terminal slider.
- `lambda` is an L2 penalty on throttle, which suppresses pairs of opposed thrusters both running
  hard to cancel each other out.
- Throttles are clamped to `[0,1]` after every step, which is the projection.
- Steps are divided by a per-thruster curvature term. This is Jacobi preconditioning and it keeps
  a single learning rate usable across grids whose thrusters differ in strength by orders of
  magnitude.
- The solution from the previous tick is used as the starting point.
- Iteration count is fixed and configurable rather than run to convergence, so the cost per tick is
  bounded. The result degrades in quality rather than in timing when the grid is large.
- If the iterate diverges the solution is zeroed for that tick rather than applied.

## Terminal controls

Per grid controller:

| Control | Range | Default | Effect |
| --- | --- | --- | --- |
| Torque Balancer | on/off | on | Throttle thrusters so net torque cancels |
| Force Weight | 0.1 - 0.9 | 0.3 | `f` above. Lower favours attitude over translation |
| Learning Rate | 0.1 - 0.5 | 0.1 | Gradient step size |
| Solver Iterations | 1 - 16 | 8 | Work budget per tick |
| Thrust Decay | 0.0 - 0.1 | 0.001 | `lambda` above |

Settings are persisted per block and synchronised in multiplayer.

## Layout

    Data/Scripts/NewtonianThrusters/NewtonianThrusters.cs   settings, terminal UI, session
                                                            component, and the solver
    Data/Storage.sbc                                        registered storage GUIDs
    modinfo.sbmi                                            Steam Workshop metadata

## Installing

Copy the folder into

    %AppData%/SpaceEngineers/Mods/

and enable it in the world's mod list. It is also on the Steam Workshop as item `3748565873`.

## Notes

Written against the Space Engineers ModAPI. The physics change is applied through
`AddForce(ADD_BODY_FORCE_AND_BODY_TORQUE, ...)` on the grid each frame; the mod does not replace
the vanilla thruster block.
