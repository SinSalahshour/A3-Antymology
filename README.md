# Antymology

Antymology is my Unity simulation project where a colony of ants learns, through evolution, to build better nests over generations.


## Project In One Minute
I spawn one queen ant (yellow) and worker ants(brwon). Every generation, ants act based on their policy parameters, the generation is scored with fitness, and the next generation is created by selecting stronger policies and mutating them.

So the loop is:
1. Simulate one generation.
2. Score ants.
3. Keep/mutate good genomes.
4. Repeat.

## What Is Happening

### 1) World setup
- The world is procedurally generated (stone/grass/mulch/acid/container blocks).
- `WorldManager` holds the true block state and chunk meshes.

### 2) Ant setup
- Exactly one queen + N workers spawn each generation.
- Every ant has health and a neural-policy genome.

### 3) Tick-by-tick simulation
On each simulation tick:
1. Health decays.
2. Acidic blocks apply extra drain (2x).
3. Each living ant decides an action from:
   - `Idle`, `Move`, `Dig`, `Eat`, `ShareHealth`, `BuildNest`
4. World and ant state update.

### 4) End of generation
- A fitness value is computed for every ant.
- Workers are sorted by fitness.
- Top workers are kept as elites.
- Remaining workers are made by mutating elites (+ occasional random genome).
- Queen policy is mutated for the next generation.

This is why behavior generally improves over time, but not strictly every single generation.

## Evolution Algorithm
Each ant policy is a compact neural network represented as a list of numbers (genome). The network reads observations (health, terrain context, queen relation, valid movement cues) and outputs action preferences.

At the end of a generation:
- better genomes are more likely to survive into the next generation,
- mutation adds variation,
- repeated selection over many generations shifts the population toward better colony behavior.

This project uses reward shaping, so fitness is not only "number of nests now". It also includes survival/resource/support terms, which helps learning start earlier in a complex environment.

### Default top-down placement
On scene start, the main camera is moved to a high position above the center of the map and aimed at the center. This gives an immediate bird's-eye view of nest growth, digging paths, hazards, and obstacles.

## HUD Guide (Top-Left Panel)
In each screenshot, use the **top-left black HUD panel**.

What each stat means:
- `Nest Blocks`: number of nest blocks currently in the world (live progress this generation)
- `Generation` / `Step`: current generation index and progress within evaluation steps
- `Last Gen Nests`: nests built by the queen in the previous generation
- `Best Fitness`: best fitness achieved in the previous generation
- `Last Gen Avg/Best Worker`: average worker fitness and best worker fitness from previous generation


For comparison quality, these last-generation stats are very useful because they summarize how the previous generation actually performed.

## Screenshot Walkthrough (gen2, gen8, gen23)

### 1) `gen2.png` (early learning)
![Generation 2](Images/Screenshots/gen2.png)

HUD in this frame:
- `Nest Blocks: 19`
- `Generation: 2  Step: 168/450`
- `Last Gen Nests: 8`
- `Best Fitness: 766.80`
- `Last Gen Avg/Best Worker: 115.98 / 263.20`

Interpretation:
- By generation 2, the colony already carries signal from generation 1 (`Last Gen Nests: 8`).
- Fitness values are no longer near zero, so selection/mutation is already finding useful behavior.
- The red nest region is forming around traversable terrain corridors.

### 2) `gen8.png` (mid-training improvement)
![Generation 8](Images/Screenshots/gen8.png)

HUD in this frame:
- `Nest Blocks: 15`
- `Generation: 8  Step: 289/450`
- `Last Gen Nests: 10`
- `Best Fitness: 964.32`
- `Last Gen Avg/Best Worker: 197.11 / 310.60`

Interpretation:
- Compared to `gen2`, the previous-generation fitness metrics are significantly higher.
- `Last Gen Nests` increased from `8` to `10`.
- Worker quality improved (`Avg/Best Worker` rose), which usually indicates better support behavior for the queen.
- You can still see fluctuations in current `Nest Blocks`; this is normal in stochastic evolutionary systems.

### 3) `gen14.png` (later-stage stronger policies)
![Generation 23 Snapshot File](Images/Screenshots/gen14.png)

HUD in this frame:
- `Nest Blocks: 23`
- `Generation: 14  Step: 320/450`
- `Last Gen Nests: 17`
- `Best Fitness: 1644.12`
- `Last Gen Avg/Best Worker: 385.51 / 607.95`

Interpretation:
- This frame shows much stronger performance statistics than earlier screenshots.
- `Last Gen Nests` and all fitness metrics are substantially higher than in `gen2` and `gen8`.
- The colony is producing larger nest structures and better aggregate behavior as evolution progresses.


## Numbers Can Still Go Up And Down
Even with evolution, per-generation metrics can fluctuate.

Main reasons:
- Mutation can degrade a previously good policy.
- Action sampling is stochastic, so the same policy can produce slightly different outcomes.
- Fitness is multi-term (not only nest count), so strategies can trade off between survival/support/building.

So what we care about is the longer-term trend in the stats.

## Ant Behavior Constraints Implemented
- Health system + death at zero
- Fixed per-tick health drain
- Mulch consumption for health restore
- No shared mulch consumption on same block
- Movement restricted to height difference <= 2
- Digging removes current support block and ant drops to lower support
- Cannot dig `ContainerBlock`
- Acidic blocks double health drain
- Zero-sum health sharing between co-located ants
- Exactly one queen ant builds nests
- Queen nest cost = 1/3 max health (default config)
- No new ants spawned during an active evaluation phase

## Main Files
- `Assets/Components/Agents/AntColonyController.cs` - simulation + evolution
- `Assets/Components/Terrain/WorldManager.cs` - world generation/state
- `Assets/Components/Configuration/ConfigurationManager.cs` - tunable parameters
- `Assets/Components/UI/FlyCamera.cs` - camera controls

## How To Run
1. Open the project in Unity Hub.
2. Use Unity `6000.3.6f1` (tested version).
3. Open `Assets/Scenes/SampleScene.unity`.
4. Press Play.

## Author
Sina Salahshour