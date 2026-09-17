# Newtonian Thrusters

A Space Engineers mod that makes thrusters apply torque about the centre of mass, and solves the
thrust allocation problem that this creates.

## The problem

For the Space Engineers mod, the problem is as follows. A space ship has thrusters. Users can place
them such that they vary in numbers, direction, strength, etc. Each gives force and torque to the
ship. From user input I get a target force and torque. This can be formulated as:

    W*u=F

Where W is 6xn. 3 dimensions for force and torque stacked (3+3) and n such thrusters. "u" is a
column vector with values 0 to 1 that is the thruster strength. F is 6x1 (force stacked on torque).
You will solve this for u, but there can be many or no solutions, or maybe you must balance between
solving force or torque. First I tried pseudo inverse to solve W, but it was difficult to balance
the priorities. Instead I used gradient descent, L2 loss function where force and torque errors can
be weighted. The user input force is also scaled down based on max theoretic input so the weight is
the same size as the torque. To prevent solutions where thrusters fight and waste energy, I added a
term to the loss that is u^2. The gradient per u is also divided by the thrusters max force^2 for
balanced learning rate per thruster. I use warm start and fewer iterations to save CPU power.
If the solution blows up or goes NaN, I set every thruster to zero for that tick instead of
applying it.

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
