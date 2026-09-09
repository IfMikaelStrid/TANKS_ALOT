# TankScript Reference


## Syntax

*One command per line. Keywords are case-insensitive. Arguments are whitespace-separated.
Numbers use `.` as decimal separator. Lines starting with `//` and blank lines are ignored.
Extra trailing tokens are ignored. `FOR` and `IF` blocks close with `END`; indentation is
cosmetic*.

## Commands

| Command | Alias | Args | Effect |
| --- | --- | --- | --- |
| `MOVE` | `FORWARD` | `<distance>` | Drive; negative backs up |
| `TURN` | `ROTATE` | `<degrees> [radius]` | Turn or arc |
| `BOOST` | - | none | Dash forward |
| `FIRE` | `SHOOT` | none | Fire forward |
| `FIND` | `SCAN`, `RADAR` | `E` | Face nearest enemy in range |
| `WAIT` | - | `<seconds>` | Pause script |
| `FOR` | - | `<count>` ... `END` | Repeat block |
| `IF` | - | `<condition>` ... [`ELSE` ...] `END` | Branch once |

## Details

- `MOVE`: uses `moveSpeed` (default 5 units/s). Range: `-1000 ... 1000`; `0` completes
  immediately.
- `TURN`: uses `rotateSpeed` (default 90 deg/s). Positive turns right, negative left. Degrees:
  `-3600 ... 3600`. Radius `0` or less spins in place; positive radius (`0 ... 100`) arcs.
- `BOOST`: uses `boostDistance` 8, `boostSpeed` 40 u/s and `boostCooldown` 2 s by default.
  Cooldown skips the command.
- `FIRE`: uses `launchForce` 20 and `shootCooldown` 2 s by default. Cooldown skips the shot.
- `FIND E`: turns toward nearest enemy within line-of-sight range, default 10. Cone angle is
  ignored. Cooldown: `findCooldown` 5 s.
- `WAIT`: pauses only the script. Range: `0 ... 60`. It does not add the normal command delay.
- `FOR`: count must be integer `0 ... 100`; `FOR 0` skips the body. Max nesting depth is 6.
- `IF`: conditions are `SPOTTED` and `NOT_SPOTTED`. `SPOTTED` checks line-of-sight range and
  angle; `FIND E` checks range only. `ELSE` is optional.
- Parse failures include unknown commands, bad numbers, missing `END`, bad conditions and bad
  `FIND` targets.

## Execution

Commands run sequentially. `MOVE`, `TURN`, `BOOST`, `FIRE` and `FIND` wait for tank completion,
then add `delayBetweenCommands` (default 0.1 s). New submissions replace the running script;
`Stop` aborts it. Scripts stop when the round or game ends. Submit cooldown is 0.5 s per client.
Destroyed tanks cannot submit.

Looping: `Passive` and `Reactive` always loop, `Active` runs once, `Dev` loops only when the
console Loop toggle is enabled.

## Limits

| Limit | Value |
| --- | --- |
| Script length | 2048 chars |
| Lines | 200 |
| Commands / nodes | 400 |
| Nesting depth | 6 |
| `FOR` count | 0 ... 100 |
| Work per pass | 20000 issued commands |
| `MOVE` distance | +/-1000 |
| `TURN` degrees | +/-3600 |
| `TURN` radius | 0 ... 100 |
| `WAIT` seconds | 0 ... 60 |

Work per pass multiplies loop bodies by count and counts an `IF` as its larger branch.

## Example

```tankscript
FOR 4
    MOVE 8
    IF SPOTTED
        FIRE
    END
    TURN 90
END
```
