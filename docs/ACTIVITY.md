# What the lantern reads

Copy sits between OOC and IC.

## States

| In LightsOn | Meaning |
| --- | --- |
| **open?** | Hours say open. No occupancy yet. |
| **open!** | Enough company. Lanterns lit. |
| **open~** | Yard and halls don't agree. Split. |
| **"open"** | Hours say open; the scan looks quiet. |
| **Lanterns lit** | Enough company on the listed plot, last ~60 minutes |
| **Quiet halls / yard quiet** | Hours said open; that layer looks empty, last ~45 minutes |
| **locked** / **locked?** | Public door is shut. `locked?` if interior reports still arrived. |
| **Extremely busy** / **Some activity** / **Some wandering** | Outdoor pocket liveliness. Not used to rank venues. |
| **In character** | Role-Playing status seen in range (a chip) |

Yard and inside list separately. The room counts extra. Either layer can be sent on its own. Quiet is always a button. Lanterns may send themselves after a short watch.

**Apartments and chambers** stay on the list. Occupancy is not checked — shared halls, no private yard. `sub` is only for those rooms.

## Company score (client, per layer)

| In LightsOn | In the game | Points |
| --- | --- | --- |
| **Patron** | another person on this layer | 1 each, cap 3 |
| **In character** | Role-Playing | +1 |
| **Seeking company** | Looking for Party / recruiting | +1 |
| **At the bench** | Melding Materia | +1 |
| **A glance** | looking at / looked at a patron | +1 total |
| **Someone reached out** | tell or party with a patron here | +1 total |
| **Voices nearby** | say with a patron here | +1 total, off unless enabled |

**Enough company** = score 3 on that layer. Friends and Free Company can be left out. Chat text is never uploaded. Yard scan is about one plot-edge (20 yalms). The room is everyone in the house.

## Log book

A closed pair: adjective + noun. Only while lanterns are lit, after ~8 minutes on the property (yard and halls are one stay). Fades in 7 days. Cap 12.

## Cooldowns

One packed write per send. Same action waits ~45 seconds. Reporter id reset waits 20 minutes. Auto lanterns taper as more unique reporters agree, and stop at six.
