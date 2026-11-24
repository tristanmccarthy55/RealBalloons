# RealBalloons

Realistic zero-pressure balloons, blimps and floating cushions for Realism Overhaul / RP-1 / RSS.
From small research balloons through Explorer II's size, NASA's 11.82 and 40 million cu ft standards and the Big 60
to a HAVOC-class Venus airship and cloud-city cushions.

RealBalloons began as an RP-1 rework of **KerBalloons** and still flies on its balloon model, textures
and sounds. The plugin is now its own: it doesn't load or call KerBalloons code. See Credits.

## Requirements

- KSP 1.12, ModuleManager, B9PartSwitch
- Made for RO / RP-1 / RSS: sizes are calibrated for RSS Earth, and tech nodes are RP-1's.
  Without RP-1 the part works in sandbox.
- Optional: TexturesUnlimited (envelope colours; ROLib needs it, so RO / RP-1 installs normally have
  it), FAR (the envelope's drag is kept out of FAR's voxels), RealFuels (tanks for compressed Helium
  and Hydrogen)

KerBalloons is **not** needed. It can stay installed alongside: the part, modules and files all have
their own names.

## Install

Download the zip from [Releases](https://github.com/tristanmccarthy55/RealBalloons/releases) and copy its
`GameData/RealBalloons` into your KSP `GameData` folder.

**Moving from the KerBalloons RP-1 pack:**
1. Run that pack's `uninstall.bat`. It restores the original KerBalloons and AirPark DLLs and removes
   `KerBalloons_RP1`.
2. Optionally uninstall KerBalloons in CKAN.
3. Install RealBalloons. Craft with the old balloon need the new part, **Aerostat Envelope**
   (Payload category). The settings are the same.

## How it flies

- **Floating.** Each size holds a fixed amount of gas. As it climbs, the gas expands until it fills the
  envelope. Above that the surplus vents out of the open bottom and lift follows the air density, so it
  floats where the air can just hold it up. Full loads float lowest; lighter loads float higher. Like a
  real zero-pressure balloon it vents rather than pops. It fails only from heat, or by tearing in fast
  airflow.
- **Climbing.** No speed limit: it climbs at whatever pace its spare lift and the envelope's drag give.
  Higher up it climbs faster, but in thinner air, so the airflow is hardest just after it leaves the
  ground. Too fast tears it (below), so a big balloon with much more lift than load can tear at launch. Real launch crews
  put in just enough gas for about 5 m/s: here that's the Gas fill slider, and the VAB's
  **Climb at launch** says how fast this craft leaves the ground and the speed that tears it.
- **Gas.** On Earth's ground a ground crew fills it for free. Anywhere else the `Hydrogen` or `Helium`
  comes from your tanks: the same amount for a size, on any planet. Each mole lifts (molar mass of the
  air minus that of the gas), with local gravity. Compared with Earth: Venus and Mars about 1.5x, Titan
  0.95x, gas giants almost nothing. Helium lifts 7% less than hydrogen and weighs twice as much. Only
  what the envelope holds where you inflate goes in (the rest would spill out of its open bottom), and
  the rest stays in your tanks: on a descent into Venus, deploy at or below where the load should
  float to put it all in, as VEGA did at 54 km. The info window says how much fits before you inflate.
- **No leaks.** The gas that goes in stays in, and there's no day and night here to cool it, so a
  balloon at float stays there. (A real zero-pressure balloon sinks at sunset and drops ballast.) Gas
  that spills above the height where the envelope is just full isn't counted as lost, so if the
  balloon later sinks below that height it lifts as before; a full load needs all its gas anyway.
- **Gas valve** (right-click in flight, any mount; action groups: Open, Close and Toggle gas valve, Hold neutral, Vent 0.5%): a valve at the top of the
  envelope, as zero-pressure balloons and gas airships have, for coming down. Gas let out is gone.
  Below the height where the envelope is just full the gas lifts the same at any height, so venting
  can't set a lower float height: a craft floats where its envelope is just full of the gas that lifts
  it. Each setting holds until you change it. **Hold neutral** lets out the surplus, the gas beyond what
  lifts the craft (carried up and spilled at float, so it would climb back there), and stays on: any lift
  the craft gains later, as its fuel burns or weight is dropped, goes too, as Zeppelins valved gas as their
  fuel burnt. It never makes the craft heavy. The craft still floats where it did, but stays at any height
  below it's taken to. That's how to take a blimp lower: vent to neutral,
  then fly it down on its elevators and engines, and it holds wherever it levels off. Nothing here leaks
  or cools the gas, so only your fuel and drops change the balance; it holds a millionth of the craft's
  weight light, so rounding can't leave it heavy. **Vent 0.5%** (an
  action group) lets out the surplus and then gas lifting 0.5% of the craft's weight, each press: a gentle, known sink, about 0.9 m/s for a blimp near the ground (1-4 m/s for each
  1% heavy, faster up high). **Open** lets gas out steadily, 0.1% of the fill a second, until you close it.
  Gas let out can't come back: only dropping weight or engines stop a sink, as on a real gas balloon. The
  valve shuts itself at 5% of the fill. The info window shows the static lift: spare (climbs back to its float),
  neutral, or short (sinking).
- **Rip panel** (right-click in flight, or an action group): a strip of the envelope torn open by a
  line, as gas balloons land with (so the envelope doesn't drag the craft over the ground) and
  scientific balloons end their flights. The gas is out in 4 s and the envelope is done: there's no
  deflating and repacking a balloon in the field. Final, so from the menu it takes a second click within
  5 s; the action group pulls it at once. To land: vent until it sinks, then pull the rip panel on
  touchdown (or cut the payload away on a decoupler and bring it down on a parachute).
- **What you see is the gas.** The envelope is drawn at the size of the gas it really holds: a small
  bubble on the pad that swells as it climbs (a blimp with ballonets is its full hull, below). A Size 0 is about 27 m across at float, Size 4 (the
  40 million cu ft) about 130 m, the Big 60 about 150 m. The envelope isn't solid, so inflating it near the ground or another craft shoves nothing.
  Inflating takes longer the more there is to fill, though bigger balloons get bigger pipes: about 5 s
  for a small one (a balloon deployed under a parachute on Venus is out in seconds), 8 s for 1000 m3,
  at most 45 s. A blimp's hull takes half as long again (21 s for 5,000 m3, 26 s for 10,000 m3) and
  comes out on a telescoping mast: a grey centre stage runs out of the base both ways and two thinner
  stages slide out of its ends, the last with domed tips, to the hull's full length over the first 60%,
  resting on the base's rim. Then the hull swells round it in its own colours, its fins growing out of the
  fabric, as the mast slides back in. Its lift comes in with the gas at a steady rate throughout.
- **Aerodynamics.** Sphere drag on the gas volume, acting where the rope is tied or at the envelope's
  centre on fixed mounts. A blimp hull is streamlined: nose-on it has an airship hull's drag (about a
  twentieth of a sphere's of the same volume when long), broadside a sphere's on its side area. Spin
  is damped.
- **Tearing.** The envelope tears when the airflow's dynamic pressure, 0.5 rho v^2, passes its rating:
  400 Pa for standard film, 200 Pa for thin film, 4,000 Pa for rubberized cotton, 6,000 Pa for the Venus fabric. The film bulges
  between load tapes about 2 m apart, so its stress goes with that pressure whatever the balloon's size
  or shape. The same pressure is a faster airspeed in thin air: standard film takes 25 m/s at sea level,
  44 m/s at 10 km and 93 m/s at 20 km, about a real blimp's top speed low down. Climbing counts. Inflate
  under a parachute or after slowing down; the info window shows the dynamic pressure against the rating.
- **Show Balloon Info** (right-click, VAB or flight) gives the envelope's mass, the gas fill, the most
  payload and its float height, the float height with no payload, the heat floor, and where this craft
  will float under all its balloons together (any mix of sizes), with how fast it climbs after
  inflating. Payload is everything but the envelopes, so the craft line shows both: "2.27 t (1.81 t
  payload)". In the VAB it plans for the planet picked in **Plan for**. In flight it adds the envelope's
  size, what it can lift where it is, airspeed, drag and buoyancy, the dynamic pressure against what
  tears it, the ballonet trim, and the cushion's stability.

## Sizes (Earth, hydrogen)

| Size | Volume (million cu ft) | Base | Balloon, standard / thin film | Payload, standard / thin film (full load floats) | Empty floats at, standard / thin | Best payload at 40 km, standard / thin film | Gas fill (H2 or He) |
|---|---|---|---|---|---|---|---|
| 0 Small research | 0.36 | 0.625 x 0.55 m | 67 kg / 42 kg | 159 kg / 184 kg (28 km) | 36 / 40 km | - / 10 kg | 17 kg or 34 kg |
| 1 Research | 1.15 | 1.25 x 0.42 m | 180 kg / 126 kg | 543 kg / 597 kg (28 km) | 37 / 40 km | 4 kg / 58 kg | 54 kg or 107 kg |
| 2 Explorer II class | 3.7 | 1.25 x 1.12 m | 549 kg / 438 kg | 1.77 t / 1.88 t (28 km) | 38 / 39 km | 119 kg / 231 kg | 173 kg or 344 kg |
| 3 NASA 11.82 MCF | 11.82 | 2.5 x 0.97 m | 1.89 t / 1.66 t | 5.52 t / 5.75 t (28 km) | 37 / 38 km | 570 kg / 797 kg | 554 kg or 1.10 t |
| 4 NASA 40 MCF | 39.57 | 3.75 x 1.69 m | 7.48 t / 7.02 t | 17.3 t / 17.8 t (28 km) | 36 / 37 km | 2.19 t / 2.65 t | 1.86 t or 3.68 t |
| 5 Big 60 class | 59.84 | 3.75 x 2.75 m | 12.1 t / 11.5 t | 25.4 t / 26.0 t (28 km) | 36 km | 3.35 t / 3.94 t | 2.81 t or 5.57 t |
| 6 HAVOC class | 119 | 5 x 3.47 m | 27.2 t / 26.3 t | 47.4 t / 48.3 t (28 km) | 35 km | 6.62 t / 7.48 t | 5.58 t or 11.1 t |
| 7 Cloud City | 361 | 10 x 3.38 m | 100 t / 98.6 t | 126 t / 128 t (28 km) | 34 km | 18.8 t / 20.4 t | 16.9 t or 33.6 t |

Sizes follow real balloons: Size 2 is Explorer II's volume, Sizes 3 and 4 NASA's 11.82 and 39.57 million cu
ft standards, Size 5 the Big 60, the largest flown. Up to Size 4 each holds about three times the gas of the one
before, so with the Gas fill slider (down to 2%) the sizes overlap widely, and a size up carries about three times
the load, or the same load higher; the Big 60 is the high-altitude step. They check against real flights: Size 4 on helium carries 3.59 t to 37 km
at 27% gas fill (NASA rates the 40 million cu ft balloon for 3,600 kg there), and Size 5 on thin film and helium
688 kg to 48.5 km at 6% (the Big 60 floated 748 kg there).

Balloon masses are at full gas fill. The plugin works them out from the film, the envelope's surface and the load
its gas lifts: film, plus caps and fittings, plus load tapes that carry the load over the balloon's height. That's
fitted to NASA's 40 million cu ft standard balloon (0.8 mil film, up to 3,600 kg to 37 km) and its Big 60 (0.4 mil,
748 kg to 48.5 km). Thin film halves the film's weight, so it lifts what it saves on top: +16% payload on Size 0,
+4% on Size 3, +1% on Size 7. The 40 km column is with a light load at the best gas fill (18%), so a lighter-built
balloon: it takes Size 3 for half a tonne, and Size 0 gets there only on thin film. Sizes 6 and 7 are bigger than
anything built; their tapes and caps for 75-230 t loads weigh a lot.

Each base holds its packed balloon (the heaviest envelope at full gas fill, folded at about 0.4 t/m3), in the
standard widths, deeper rather than wider for the small sizes.

Helium lifts about 7% less.

## Gas fill and envelope volume (VAB)

Each balloon has three settings that do different jobs, like real balloon design:

- **Size (0-7)** sets the most gas it can carry, and so the most it can lift. It also sets the base
  hardware, and it's what the tech tree unlocks.
- **Gas fill**, 2-100% of that, sets how much it lifts. Less gas needs less from the tanks, and a
  light load climbs gently on its own. It's also the load the balloon is built for: less gas, lighter
  tapes and caps, a lighter balloon. That's how real high-altitude balloons work (big and lightly
  loaded), and how to fly high here: a big size, a low gas fill, and thin film.
- **Envelope volume**, 2-100% of the standard envelope, sets how high it floats. A balloon floats once
  its envelope is full: the gas expands as it climbs, lift stays the same while the envelope is
  slack, and once it's full lift falls with the air density. A smaller envelope fills sooner, in
  denser air, so it floats lower. It also weighs less (less film, shorter tapes), so it lifts more.

**Plan for** picks the planet the readouts plan for: any whose air some balloon here could float in (Earth, Venus,
Mars and Titan in RSS; gas giants, the Sun and near-airless worlds such as Pluto are left out). On each the balloon
inflates at the lowest height it survives: the ground, or above the heat on Venus. **Floats** gives the most payload
and its float height, and the empty float height. **Fails below** (when it applies) is the lowest height it survives
there and why: heat, or Venus's acid clouds. **Climb at launch** is this whole craft under all its balloons: how fast
it leaves the ground and the speed that tears the weakest envelope there, with a warning when it would tear. On
Earth with hydrogen at full gas, a full load floats at:

| Envelope volume | 100% | 50% | 25% | 15% | 10% |
|---|---|---|---|---|---|
| Full load floats at | 28 km | 24 km | 20 km | 17 km | 14 km |
| Size 6 payload | 47.4 t | 51.7 t | 55.4 t | 57.9 t | 59.7 t |

Lighter loads float higher (Size 6 empty: 35 km at 100%, 24 km at 10%). A load over the most payload doesn't lift at all: the
gas can't hold it up even before the envelope fills.

## Envelopes

| Envelope | Use | Rated to | Tears at |
|---|---|---|---|
| Polyethylene (hydrogen or helium) | NASA's standard 0.8 mil (20 micron) film. Earth, and Mars from the surface (payload about halves every 5 km up; thin film does a little better). Resists sulfuric acid only when cold: on Venus it survives the clouds above ~58 km (below 20 C), so a light high-altitude mission works, but not deeper. | 80 C | 400 Pa: 25 m/s at sea level, 44 m/s at 10 km |
| Rubberized cotton (hydrogen or helium) | The fabric of early airships and of Explorer II (220 g/m2: its 3.7 million cu ft envelope weighed 2.9 t), from the start. Heavy but strong: the early airship hull. With ballonets and a small hull it flies fast low down, carrying less than a polyethylene hull of the same size (a quarter less on Size 0, a few % on big hulls); as a high-altitude balloon it's heavy. No acid resistance: Venus's sulfuric acid clouds destroy it below ~70 km. | 80 C | 4,000 Pa: 80 m/s at sea level, 94 m/s at 3 km |
| Thin-film polyethylene | Half as thick, 0.4 mil (10 micron), like the Big 60's. The balloon is 25 kg (Size 0) to 1.58 t (Size 7) lighter, so it lifts that much more and floats empty up to 3 km higher. That matters most for light loads up high: at 40 km (18% gas fill) Size 3 carries 797 kg instead of 570 kg, and Size 0 gets there at all (10 kg). Resists acid only when cold, like standard film. | 80 C | 200 Pa: 18 m/s at sea level, 31 m/s at 10 km |
| Teflon-coated fabric, Venus (helium or hydrogen) | A small, strong, acid-proof envelope (212 g/m2 like VEGA's, about the same as rubberized cotton) for dense air, like the VEGA balloons at Venus in 1985. On Venus a full load floats near 53 km, empty 66-69 km; Size 6 floats 75.1 t at 55 km on helium (78.9 t on hydrogen, which can't burn in CO2). Also works on Titan. On Earth it's the airship hull: its small envelope fills about 2 km up, and it takes 98 m/s at sea level (a Size 3 hull is 8,080 m3; it carries a few % less than a cotton hull that size, as its load tapes are rated for Venus's heavier air) | 160 C (fails below ~40 km on Venus) | 6,000 Pa: 47 m/s at 40 km on Venus, 98 m/s at 55 km |

## Mounts (right-click in the VAB)

- **On a rope.** The classic balloon: lift acts where the rope is tied.
- **Fixed to the part.** No rope: the envelope sits on the part and turns with the craft. Polyethylene
  blimps are a liberty (real airships use far heavier fabric); for a tough, fast hull use rubberized cotton
  (from the start) or the Teflon fabric.
  - **Blimp length 1-4.** Above 1 the envelope lies on its side as an airship hull, longer and slimmer
    with the same volume (at 3 it's about 3.5 times as long as it is wide). Placed unrotated, the round nose
    points forward and the tail back; rotate the part to aim it. The part sits under its centre of buoyancy. In the
    VAB the packed cap becomes a small airship outline, so you can see which way it faces.
    **Tail fins and controls:** the four tail fins (top, bottom, sides) are real lifting surfaces at the
    size they're drawn, growing with the gas and stretching with the hull (on a Size 0 hull at length 3,
    about 53 m² each). Their rear third is hinged: rudders on the top and bottom fins, elevators on the
    sides, moving up to 20 degrees with your pitch and yaw, SAS and trim included; SAS knows how much they can
    do. Full deflection is a real elevator's worth, about half the force of the fin turned broadside, in
    line with FAR's control surfaces. Like KSP's own they answer the craft's control point: on a blimp,
    have the probe core's top point along the hull (or Control From Here on a part that faces forward), or
    pitch and yaw won't be the hull's (with the control point facing up, Q and E turn it). Everything goes
    with the dynamic pressure, so they steer at cruise and do nothing at a hover, where a real blimp turns
    with its engines; no charge, as early airships pulled theirs on cables. The fins keep the hull pointing
    along its path and damp its turning (their lift raised by the hull's upwash at their roots); the hull
    itself tends to turn broadside to the air (the Munk moment, taken at 60% of the ideal value, as
    measured on real hulls, and less at large angles as the flow leaves its tail), and the fins as drawn
    outweigh it, so it's stable like most blimps. The hull's own crossflow drag damps its pitch and yaw as
    it swings, at a hover too (each length of it pushed sideways through the air; the air it carries along
    is counted in its inertia, not as damping). Turns are
    slow and wide, as blimps' are: full rudder on a Size 0 hull at length 3 is about 2 degrees a second at
    9 m/s, quicker the faster you go; differential thrust helps. **Elevators** and **Rudders** limit each
    one's authority (100% to start). They don't roll the hull: real blimps don't roll on their fins, the
    gondola hanging under them rights them. The hull turns slowly, as a real one's mass and the air it carries make it: an
    elevator input takes several seconds to settle, and it swings no quicker at speed than at a hover. A
    light gondola is a weak pendulum, so it pitches and rolls more for the same controls (full elevator
    about 16 degrees with a quarter-tonne gondola under a Size 0 hull), and climbing or sinking tips the
    hull towards its path like a weathervane (Hold level trims that out); a heavier gondola holds it level.
    **Ballonets** (on by default): like a real blimp, air bags inside the hull keep it full at any height,
    so it's drawn, dragged and sways at its full size from the ground up. The ballonet air weighs what it
    displaces, so lift is the gas's, the same law as the balloon: constant as it climbs until the gas
    fills the hull (its pressure height: the full-load float height in the Floats readout), then the
    surplus vents. **Envelope volume** sizes the hull, and so sets the pressure height: at full gas,
    about sea level at 2%, 14 km at 10%, 28 km at 100%. A hull sized for high altitude is huge and draggy
    low down, as it would be. The gas lies along the top of the hull over the ballonet air, so the lift
    acts at its centre: low down, where it's a thin layer, that hangs the gondola much more firmly (the
    bottom-heaviness real blimps fly with), easing to the hull's centre at the pressure height, and in
    towards the axis as the hull pitches (curtains inside keep the gas from running to the high end). Off, the
    blimp launches flaccid and takes its shape as the gas expands, as
    stratospheric airships such as HiSentinel do.
    **Hull preview** (VAB, with ballonets): shows the hull inflated, as it will fly: size, shape, fins,
    colours and flag. It's only a picture: it isn't saved, has no collider, and isn't counted in the craft's
    size for launch-site limits. The CoM marker doesn't move with it: the envelope's mass is counted at the
    part, in the VAB and in flight.
    **Ballonet trim:** like a real blimp blowing air between its fore and aft ballonets, the trim turns
    the hull nose up or down: hugely low down, where the ballonets hold tonnes of air, and little at the
    pressure height, where the gas fills the hull (up to 40% of the ballonet air moved half the hull's
    length, plus the lift's turn over 15% of the hull's length at any height), by its lever across the
    vertical: all of it level, none standing on end. Once the hull is fully inflated (each inflation) it
    balances the craft's centre of mass under the hull. **Ballonet trim** (VAB or flight): **Hold level**
    then slowly trims the hull level, so dropping tanks, or the fins' drag tipping the hull as it climbs,
    are trimmed out; it waits while you steer, so it doesn't undo your elevators. **Hold** keeps it where
    it is. **Off** never trims: the ballonets just keep the hull full, and it pitches wherever its weight
    puts it (the gas along the top of the hull still makes it bottom-heavy). The info window shows the
    trim, and warns when it's at its limit.
  - **Cushion.** For a payload sitting on top of the envelope, like a cloud city. A creative liberty: a
    zero-pressure envelope would fold around a load on top, and real cloud-city designs use superpressure
    or rigid aerostats. It flies the same here as a superpressure one would (no leaks, no day and night).
    Tipped more than 60 degrees from upright for half a second, it's torn by its keel (the keel's lines
    fan out to a ring under the envelope, and its whole weight ends up on one side); a warning comes at
    45. The angle is `keelTearAngle` in the part config, and the info window shows the tilt. Fit the part cap-down
    under the craft (the VAB readout warns if the cap points up). The envelope comes out below,
    squashed wider than tall by the load (`cushionShape` 0.65). It's only stable with the centre of mass
    below the envelope's centre, so a counterweight lowers out of its far end on a long keel as it
    inflates. **Keel counterweight** is 0-30% of the size's lift (default 15%, which is 11 t on
    Size 6); its mass shows in the VAB and comes out of your payload. **Keel length** is 2-12 envelope
    radii (default 8). The keel acts as a stiff boom drawn as a tether: a real rope can't hold a weight
    out on a lever. The info window shows how far below the envelope's centre the centre of mass is.
    The cushion is solid: as it inflates on the ground it lifts the craft off the floor, the craft can
    rest and land on it, and kerbals can walk on it (the keel and weight have no collider, so they sink
    into the ground while it sits there). Landing on it harder than the part's crash tolerance (8 m/s)
    still bursts it.
  - Fixed mounts sway like a big balloon. The envelope, its gas, the air it pushes aside and the keel
    weight make the craft hard to turn, so it sways slowly (at most one swing every 4 seconds). KSP
    doesn't know about that inertia, so reaction wheels can lean it more than they could for real:
    switch SAS on only when it's upright.

**Envelope colour** (VAB): TexturesUnlimited's recolouring, as on ROLib parts and Procedural Fairings.
**Envelope** in the part menu picks a pattern: **Natural film** (one colour), **Load tapes** (the tapes
along every gore seam picked out), **Striped gores**, **Equator band** or **Harlequin** (striped gores
that swap colours across the band). **Open Recoloring GUI** sets its colours, each with gloss and metal:
any colour, TU's palette, or the balloon films in the **RealBalloons** group (natural polyethylene,
aluminised Mylar, white, silver, black, red, orange, yellow, green, blue, purple). It copies and pastes
between parts like any TU part. It colours the envelope and the blimp fins, and the packed cap shows the
pattern in miniature in the VAB; the fabric's seams and shading stay. Craft saved with the old colour
settings keep their colour. Without TU the envelope is natural film.

**Envelope flag** (VAB): **Choose envelope flag** opens KSP's flag browser and paints that flag on the envelope
(and its symmetry twins), whatever the mission flag is; **Use mission flag on envelope** puts it back. The flag
shows once the envelope inflates.

The patterns are masks made by `Source/Tools/make_masks.py` (Python with numpy and Pillow); its header
explains the mapping, and new patterns are a few lines there plus a `KSP_TEXTURE_SET` in `RealBalloon.cfg`.
The model maps the fabric texture straight down the envelope, a disc, where band edges are circles that cut
across the texels and showed as teeth. So the plugin unrolls the envelope at load: its texture runs once round
it and from end to end, evenly along the envelope, with its own copy of the KerBalloons fabric unrolled the
same way. Band edges run along texel rows and gore seams along columns, so they stay straight at any distance,
even on a long blimp. The packed cap gets a texture of its own too, so its miniature of the pattern stays sharp
up close.

## Tech (RP-1)

| Unlock | Node | Real-world reference |
|---|---|---|
| Size 0 | Start | polyethylene research balloons, from 1947 |
| Rubberized cotton | Start | the fabric of airships from the 1900s and of Explorer II (1935); US Navy blimps flew cotton envelopes into the 1950s |
| Size 1 | Post-War Materials | - |
| Size 2 | Early Materials (1952-55) | Explorer II's volume (3.7 million cu ft, 22 km in 1935); the 3.2 million cu ft Super Skyhook flew in polyethylene in 1954 |
| Size 3 | Early Human Spaceflight (1961-63) | NASA's 11.82 million cu ft standard; 10 million cu ft balloons from 1960 (Strato-Lab V, 34.7 km, 1961) |
| Size 4 | Lunar Exploration (1967-71) | NASA's 39.57 million cu ft standard; 34 million cu ft flew in 1970, 47.8 million in 1972 |
| Thin film | Space Station Era (1972-80) | 0.4 mil like the Big 60's; 0.35 mil film flew on a 47.8 million cu ft balloon in 1972 |
| Venus envelope | Spaceplanes Era (1981-85) | VEGA's Teflon-coated balloons at Venus, 1985 |
| Size 5 | International Cooperation (1998-2008) | NASA's Big 60, the largest balloon flown (2002) |
| Size 6 | Commercial Spaceflight (2009-18) | twice the Big 60; with the Venus envelope, about HAVOC's crewed airship (2015 study) |
| Size 7 | Near Future (2028-50) | - |

## What changed from KerBalloons

Kept: the balloon model, texture and sounds, the inflate action, the flag on the envelope, and the
B9PartSwitch size switching. Dropped: deflating and repacking in flight (no real balloon does it; a rip
panel ends the flight instead).

New: zero-pressure physics and float heights, true-to-gas envelope size, gas from tanks, heat and
tear failure, envelope aerodynamics, Sizes 4-7, envelope materials, RP-1 tech placement, the fixed,
blimp and cushion mounts with keel, sway inertia, and the info window.

Dropped: the per-planet stats table, the planet cap pictures, the climb-speed controller, the
envelope's sphere collider (the cushion has its own, shaped like it), and the KerBalloons code, settings and libraries (ClickThroughBlocker,
ToolbarControl, SpaceTuxUtility). The texture's teal glow ring is now metal grey.

## Building

`Source/` has the plugin source. It compiles against your own KSP's `KSP_x64_Data/Managed` DLLs (referenced,
never copied) and writes `GameData/RealBalloons/Plugins/RealBalloons.dll`.

- **Windows**, with the .NET SDK (8 or later):

      dotnet build Source/RealBalloons.csproj -c Release -p:KSPDIR="C:/path/to/Kerbal Space Program"

- **Linux / macOS**, with Mono:

      Source/build.sh "<KSP>/KSP_x64_Data/Managed"

## Credits

RealBalloons is by **tristanmccarthy55** (GitHub). It's a heavily rewritten fork of **KerBalloons**,
originally by **JoePatrick1** and maintained by **linuxgurugamer**, and still uses KerBalloons' balloon
model, base textures, sounds and inflation framework.

- **JoePatrick1**: the original KerBalloons, including the model, texture and sounds.
- **linuxgurugamer**: maintains KerBalloons.
- **Fengist / DataInterlock**: KerBalloons Reinflated, whose balloon module this plugin began as.
- **R3adux**: the original KerBalloons RP-1 patch that this grew from.

RealBalloons isn't affiliated with or endorsed by the KerBalloons authors.

## Licence

MIT, including the KerBalloons code and assets it's built on. See `LICENSE.txt`.
