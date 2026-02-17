using Antymology.Terrain;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antymology.Agents
{
    /// <summary>
    /// Runs the ant simulation and uses neuroevolution to improve behavior over generations.
    /// </summary>
    public class AntColonyController : MonoBehaviour
    {
        private enum AntAction
        {
            Idle = 0,
            Move = 1,
            Dig = 2,
            Eat = 3,
            ShareHealth = 4,
            BuildNest = 5,
        }

        private const int ObservationSize = 24;
        private const int HiddenSize = 10;
        private const int ActionCount = 6;
        private const int MoveDirectionCount = 4;
        private const int NetworkOutputSize = ActionCount + MoveDirectionCount;
        private const int ParameterCount =
            (ObservationSize * HiddenSize) + HiddenSize +
            (HiddenSize * NetworkOutputSize) + NetworkOutputSize;

        private struct PolicyDecision
        {
            public AntAction Action;
            public int MoveDirectionIndex;
        }

        private struct DirectionMoveInfo
        {
            public bool IsValid;
            public Vector3Int Cell;
            public AbstractBlock Block;
        }

        private class NeuralGenome
        {
            public readonly float[] Parameters = new float[ParameterCount];

            public NeuralGenome Clone()
            {
                NeuralGenome clone = new NeuralGenome();
                Array.Copy(Parameters, clone.Parameters, Parameters.Length);
                return clone;
            }
        }

        private class AntAgent
        {
            public bool IsQueen;
            public NeuralGenome Genome;
            public Vector3Int Cell;
            public float Health;
            public float MaxHealth;
            public bool IsDead;
            public float Fitness;
            public int StepsAlive;
            public int MulchConsumed;
            public int BlocksDug;
            public int NestsBuilt;
            public float HealthShared;
            public GameObject Visual;
        }

        private static readonly Vector2Int[] MovementDirections =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };

        private readonly List<AntAgent> Agents = new List<AntAgent>();
        private readonly List<NeuralGenome> WorkerGenomes = new List<NeuralGenome>();
        private readonly List<AntAgent> LiveActionOrder = new List<AntAgent>(128);

        private readonly float[] ObservationBuffer = new float[ObservationSize];
        private readonly float[] HiddenBuffer = new float[HiddenSize];
        private readonly float[] OutputBuffer = new float[NetworkOutputSize];
        private readonly bool[] ActionMaskBuffer = new bool[ActionCount];
        private readonly DirectionMoveInfo[] DirectionInfoBuffer = new DirectionMoveInfo[MoveDirectionCount];

        private NeuralGenome QueenGenome;
        private AntAgent QueenAgent;
        private WorldManager World;
        private ConfigurationManager Config;
        private System.Random RNG;
        private GUIStyle HUDStyle;
        private float TickAccumulator;
        private int GenerationIndex;
        private int StepInGeneration;

        private float LastGenerationBestFitness;
        private float LastGenerationAverageFitness;
        private float LastGenerationBestWorkerFitness;
        private int LastGenerationNestBlocks;

        private void Start()
        {
            World = WorldManager.Instance;
            Config = ConfigurationManager.Instance;
            if (World == null || Config == null)
            {
                Debug.LogError("AntColonyController could not find WorldManager or ConfigurationManager.");
                enabled = false;
                return;
            }

            RNG = new System.Random(Config.Seed + 8128);

            InitializeGenomePopulation();
            StartNextGeneration();
        }

        private void Update()
        {
            if (World == null || Config == null)
                return;

            float tickSeconds = Mathf.Max(0.01f, Config.Ant_Tick_Seconds);
            TickAccumulator += Time.deltaTime;

            while (TickAccumulator >= tickSeconds)
            {
                TickAccumulator -= tickSeconds;
                TickGeneration();
            }
        }

        private void OnGUI()
        {
            if (World == null || Config == null)
                return;

            if (HUDStyle == null)
            {
                HUDStyle = new GUIStyle(GUI.skin.label);
                HUDStyle.fontSize = 14;
                HUDStyle.normal.textColor = Color.white;
            }

            Rect panel = new Rect(12, 12, 380, 135);
            GUI.Box(panel, string.Empty);
            GUILayout.BeginArea(panel);
            GUILayout.Space(8);
            GUILayout.Label("Nest Blocks: " + World.NestBlockCount, HUDStyle);
            GUILayout.Label("Generation: " + GenerationIndex + "  Step: " + StepInGeneration + "/" + Config.Ant_Evaluation_Steps, HUDStyle);
            GUILayout.Label("Alive Ants: " + CountAliveAgents() + "/" + Agents.Count, HUDStyle);
            GUILayout.Label("Last Gen Nests: " + LastGenerationNestBlocks + "  Best Fitness: " + LastGenerationBestFitness.ToString("0.00"), HUDStyle);
            GUILayout.Label("Last Gen Avg/Best Worker: " + LastGenerationAverageFitness.ToString("0.00") + " / " + LastGenerationBestWorkerFitness.ToString("0.00"), HUDStyle);
            GUILayout.EndArea();
        }

        private void InitializeGenomePopulation()
        {
            WorkerGenomes.Clear();

            int workerCount = Mathf.Max(1, Config.Ant_Worker_Count);
            for (int i = 0; i < workerCount; i++)
                WorkerGenomes.Add(CreateRandomGenome());

            QueenGenome = CreateRandomGenome();
        }

        private void StartNextGeneration()
        {
            bool shouldResetWorld = GenerationIndex > 0 && Config.Ant_Reset_World_Each_Generation;
            if (shouldResetWorld)
                World.ResetWorldToInitialState();

            ClearAgentVisuals();
            Agents.Clear();
            QueenAgent = null;
            StepInGeneration = 0;
            GenerationIndex++;

            HashSet<Vector3Int> occupiedSpawns = new HashSet<Vector3Int>();

            if (TryFindSpawnCell(occupiedSpawns, out Vector3Int queenSpawn))
            {
                occupiedSpawns.Add(queenSpawn);
                QueenAgent = CreateAgent(true, QueenGenome.Clone(), queenSpawn, 0);
                Agents.Add(QueenAgent);
            }

            for (int i = 0; i < WorkerGenomes.Count; i++)
            {
                Vector3Int spawnCell = default;
                bool foundNearQueen = QueenAgent != null && TryFindSpawnNear(QueenAgent.Cell, occupiedSpawns, 8, out spawnCell);
                if (!foundNearQueen && !TryFindSpawnCell(occupiedSpawns, out spawnCell))
                    break;

                occupiedSpawns.Add(spawnCell);
                Agents.Add(CreateAgent(false, WorkerGenomes[i].Clone(), spawnCell, i));
            }

            if (Agents.Count == 0)
                Debug.LogWarning("No valid spawn cells were found for this generation.");
        }

        private void TickGeneration()
        {
            if (Agents.Count == 0)
                return;

            if (StepInGeneration >= Mathf.Max(1, Config.Ant_Evaluation_Steps) || CountAliveAgents() == 0)
            {
                EndGenerationAndEvolve();
                return;
            }

            BuildLiveActionOrder();

            for (int i = 0; i < LiveActionOrder.Count; i++)
            {
                AntAgent agent = LiveActionOrder[i];
                if (agent.IsDead)
                    continue;

                ApplyHealthDecay(agent);
                if (agent.IsDead)
                    continue;

                int sameCellCount = CountLivingAgentsAtCell(agent.Cell);
                PolicyDecision decision = DecideAction(agent, sameCellCount);
                ExecuteAction(agent, decision, sameCellCount);

                agent.StepsAlive++;
                UpdateVisualPosition(agent);
            }

            StepInGeneration++;
        }

        private void EndGenerationAndEvolve()
        {
            int queenNests = 0;
            for (int i = 0; i < Agents.Count; i++)
            {
                if (Agents[i].IsQueen)
                {
                    queenNests = Agents[i].NestsBuilt;
                    break;
                }
            }

            float totalFitness = 0f;
            float bestFitness = float.MinValue;
            float bestWorkerFitness = float.MinValue;

            for (int i = 0; i < Agents.Count; i++)
            {
                Agents[i].Fitness = CalculateFitness(Agents[i], queenNests);
                totalFitness += Agents[i].Fitness;
                bestFitness = Mathf.Max(bestFitness, Agents[i].Fitness);

                if (!Agents[i].IsQueen)
                    bestWorkerFitness = Mathf.Max(bestWorkerFitness, Agents[i].Fitness);
            }

            LastGenerationNestBlocks = queenNests;
            LastGenerationBestFitness = bestFitness > float.MinValue ? bestFitness : 0f;
            LastGenerationAverageFitness = Agents.Count > 0 ? totalFitness / Agents.Count : 0f;
            LastGenerationBestWorkerFitness = bestWorkerFitness > float.MinValue ? bestWorkerFitness : 0f;

            Debug.Log(
                "[AntColony] Generation " + GenerationIndex +
                " complete | nests=" + LastGenerationNestBlocks +
                " | best=" + LastGenerationBestFitness.ToString("0.00") +
                " | avg=" + LastGenerationAverageFitness.ToString("0.00") +
                " | bestWorker=" + LastGenerationBestWorkerFitness.ToString("0.00")
            );

            EvolvePopulation(queenNests);
            StartNextGeneration();
        }

        private void EvolvePopulation(int queenNests)
        {
            List<AntAgent> workers = new List<AntAgent>();
            AntAgent currentQueen = null;

            for (int i = 0; i < Agents.Count; i++)
            {
                if (Agents[i].IsQueen)
                    currentQueen = Agents[i];
                else
                    workers.Add(Agents[i]);
            }

            workers.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));

            int workerCount = Mathf.Max(1, Config.Ant_Worker_Count);
            int eliteCount = Mathf.Clamp(Config.Ant_Elite_Count, 1, workerCount);
            List<NeuralGenome> nextWorkerGenomes = new List<NeuralGenome>(workerCount);

            if (workers.Count == 0)
            {
                for (int i = 0; i < workerCount; i++)
                    nextWorkerGenomes.Add(CreateRandomGenome());
            }
            else
            {
                int elitesToKeep = Mathf.Min(eliteCount, workers.Count);
                for (int i = 0; i < elitesToKeep; i++)
                    nextWorkerGenomes.Add(workers[i].Genome.Clone());

                while (nextWorkerGenomes.Count < workerCount)
                {
                    if (RNG.NextDouble() < 0.15)
                    {
                        nextWorkerGenomes.Add(CreateRandomGenome());
                        continue;
                    }

                    NeuralGenome parent = nextWorkerGenomes[RNG.Next(0, elitesToKeep)];
                    nextWorkerGenomes.Add(MutateGenome(parent, Config.Ant_Mutation_Strength));
                }
            }

            WorkerGenomes.Clear();
            WorkerGenomes.AddRange(nextWorkerGenomes);

            if (currentQueen == null)
            {
                QueenGenome = MutateGenome(QueenGenome, Config.Ant_Mutation_Strength);
            }
            else
            {
                float mutation = queenNests > 0
                    ? Config.Ant_Mutation_Strength * 0.25f
                    : Config.Ant_Mutation_Strength * 1.1f;

                QueenGenome = MutateGenome(currentQueen.Genome, mutation);
                if (queenNests == 0 && RNG.NextDouble() < 0.3)
                    QueenGenome = CreateRandomGenome();
            }
        }

        private AntAgent CreateAgent(bool isQueen, NeuralGenome genome, Vector3Int spawnCell, int workerIndex)
        {
            float maxHealth = isQueen ? Config.Ant_Queen_Max_Health : Config.Ant_Worker_Max_Health;
            AntAgent agent = new AntAgent
            {
                IsQueen = isQueen,
                Genome = genome,
                Cell = spawnCell,
                MaxHealth = maxHealth,
                Health = maxHealth,
                IsDead = false,
            };

            agent.Visual = CreateAgentVisual(agent, workerIndex);
            UpdateVisualPosition(agent);
            return agent;
        }

        private GameObject CreateAgentVisual(AntAgent agent, int workerIndex)
        {
            GameObject visual;
            if (World.antPrefab != null)
            {
                visual = Instantiate(World.antPrefab, transform);
            }
            else
            {
                PrimitiveType type = agent.IsQueen ? PrimitiveType.Sphere : PrimitiveType.Capsule;
                visual = GameObject.CreatePrimitive(type);
                visual.transform.parent = transform;
            }

            visual.name = agent.IsQueen ? "QueenAnt" : "WorkerAnt_" + workerIndex;
            visual.transform.localScale = agent.IsQueen ? new Vector3(0.95f, 0.95f, 0.95f) : new Vector3(0.55f, 0.55f, 0.55f);

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            Color targetColor = agent.IsQueen ? new Color(0.95f, 0.62f, 0.2f) : new Color(0.42f, 0.25f, 0.1f);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].material != null)
                    renderers[i].material.color = targetColor;
            }

            return visual;
        }

        private void ClearAgentVisuals()
        {
            for (int i = 0; i < Agents.Count; i++)
            {
                if (Agents[i].Visual != null)
                    Destroy(Agents[i].Visual);
            }
        }

        private PolicyDecision DecideAction(AntAgent agent, int sameCellCount)
        {
            bool canEat = CanConsumeMulch(agent, sameCellCount);
            bool canDig = CanDig(agent);
            bool canShare = CanShareHealth(agent, sameCellCount);
            bool canBuild = CanBuildNest(agent);
            float healthRatio = agent.MaxHealth > 0f ? agent.Health / agent.MaxHealth : 0f;

            int validMoveCount = BuildDirectionMoveInfo(agent, DirectionInfoBuffer);

            // Strong priors so early generations produce visible nest behavior.
            if (agent.IsQueen)
            {
                float buildCost = agent.MaxHealth * Mathf.Clamp01(Config.Ant_Queen_Nest_Cost_Fraction);
                if (canBuild && agent.Health >= buildCost * 1.1f)
                {
                    return new PolicyDecision { Action = AntAction.BuildNest, MoveDirectionIndex = -1 };
                }
                if (canEat && healthRatio < 0.7f)
                {
                    return new PolicyDecision { Action = AntAction.Eat, MoveDirectionIndex = -1 };
                }
            }
            else
            {
                if (canShare && QueenAgent != null && !QueenAgent.IsDead && agent.Cell == QueenAgent.Cell)
                {
                    return new PolicyDecision { Action = AntAction.ShareHealth, MoveDirectionIndex = -1 };
                }
                if (canEat && healthRatio < 0.62f)
                {
                    return new PolicyDecision { Action = AntAction.Eat, MoveDirectionIndex = -1 };
                }
            }

            BuildObservation(agent, sameCellCount, validMoveCount, canEat, canDig, canShare, canBuild, DirectionInfoBuffer, ObservationBuffer);
            EvaluateNetwork(agent.Genome, ObservationBuffer, HiddenBuffer, OutputBuffer);

            ActionMaskBuffer[(int)AntAction.Idle] = true;
            ActionMaskBuffer[(int)AntAction.Move] = validMoveCount > 0;
            ActionMaskBuffer[(int)AntAction.Dig] = canDig;
            ActionMaskBuffer[(int)AntAction.Eat] = canEat;
            ActionMaskBuffer[(int)AntAction.ShareHealth] = canShare;
            ActionMaskBuffer[(int)AntAction.BuildNest] = canBuild;

            int actionIndex = SampleMaskedLogits(OutputBuffer, ActionMaskBuffer, ActionCount, 0.8f);
            PolicyDecision decision = new PolicyDecision
            {
                Action = (AntAction)actionIndex,
                MoveDirectionIndex = -1,
            };

            if (decision.Action == AntAction.Move)
                decision.MoveDirectionIndex = SampleDirection(OutputBuffer, ActionCount, DirectionInfoBuffer, 0.75f);

            return decision;
        }

        private void ExecuteAction(AntAgent agent, PolicyDecision decision, int sameCellCount)
        {
            switch (decision.Action)
            {
                case AntAction.Move:
                    TryMove(agent, decision.MoveDirectionIndex);
                    break;
                case AntAction.Dig:
                    TryDig(agent);
                    break;
                case AntAction.Eat:
                    TryConsumeMulch(agent, sameCellCount);
                    break;
                case AntAction.ShareHealth:
                    TryShareHealth(agent);
                    break;
                case AntAction.BuildNest:
                    TryBuildNest(agent);
                    break;
                default:
                    break;
            }
        }

        private void ApplyHealthDecay(AntAgent agent)
        {
            float drain = Mathf.Max(0f, Config.Ant_Base_Health_Drain);
            AbstractBlock standingOn = World.GetBlock(agent.Cell.x, agent.Cell.y, agent.Cell.z);
            if (standingOn is AcidicBlock)
                drain *= 2f;

            agent.Health -= drain;
            if (agent.Health <= 0f)
                KillAgent(agent);
        }

        private bool TryMove(AntAgent agent, int preferredDirection)
        {
            int validCount = BuildDirectionMoveInfo(agent, DirectionInfoBuffer);
            if (validCount <= 0)
                return false;

            int heuristicDirection = GetHeuristicMoveDirection(agent, DirectionInfoBuffer);
            if (heuristicDirection >= 0)
                preferredDirection = heuristicDirection;

            if (preferredDirection >= 0 && preferredDirection < MoveDirectionCount && DirectionInfoBuffer[preferredDirection].IsValid)
            {
                agent.Cell = DirectionInfoBuffer[preferredDirection].Cell;
                return true;
            }

            int target = RNG.Next(0, validCount);
            int seen = 0;
            for (int i = 0; i < MoveDirectionCount; i++)
            {
                if (!DirectionInfoBuffer[i].IsValid)
                    continue;

                if (seen == target)
                {
                    agent.Cell = DirectionInfoBuffer[i].Cell;
                    return true;
                }

                seen++;
            }

            return false;
        }

        private bool TryConsumeMulch(AntAgent agent, int sameCellCount)
        {
            if (!CanConsumeMulch(agent, sameCellCount))
                return false;

            int previousY = agent.Cell.y;
            World.SetBlock(agent.Cell.x, agent.Cell.y, agent.Cell.z, new AirBlock());
            agent.Health = Mathf.Min(agent.MaxHealth, agent.Health + Mathf.Max(0f, Config.Ant_Mulch_Health_Restore));
            agent.MulchConsumed++;

            if (!TryMoveDownAfterRemovingSupport(agent, previousY))
                KillAgent(agent);

            return true;
        }

        private bool TryDig(AntAgent agent)
        {
            if (!CanDig(agent))
                return false;

            int previousY = agent.Cell.y;
            World.SetBlock(agent.Cell.x, agent.Cell.y, agent.Cell.z, new AirBlock());
            agent.BlocksDug++;

            if (!TryMoveDownAfterRemovingSupport(agent, previousY))
                KillAgent(agent);

            return true;
        }

        private bool TryShareHealth(AntAgent donor)
        {
            if (donor.IsDead || donor.IsQueen)
                return false;

            AntAgent receiver = null;
            if (QueenAgent != null && !QueenAgent.IsDead && donor.Cell == QueenAgent.Cell && QueenAgent.Health < QueenAgent.MaxHealth)
            {
                receiver = QueenAgent;
            }
            else
            {
                for (int i = 0; i < Agents.Count; i++)
                {
                    if (Agents[i].IsDead || ReferenceEquals(Agents[i], donor) || Agents[i].Cell != donor.Cell)
                        continue;

                    if (receiver == null || Agents[i].Health < receiver.Health)
                        receiver = Agents[i];
                }
            }

            if (receiver == null)
                return false;

            float transfer = Mathf.Min(
                Mathf.Max(0f, Config.Ant_Health_Transfer_Amount),
                donor.Health - 1f,
                receiver.MaxHealth - receiver.Health
            );

            if (transfer <= 0f)
                return false;

            donor.Health -= transfer;
            receiver.Health += transfer;
            donor.HealthShared += transfer;
            return true;
        }

        private bool TryBuildNest(AntAgent queen)
        {
            if (!CanBuildNest(queen))
                return false;

            float cost = queen.MaxHealth * Mathf.Clamp01(Config.Ant_Queen_Nest_Cost_Fraction);
            World.SetBlock(queen.Cell.x, queen.Cell.y, queen.Cell.z, new NestBlock());
            queen.Health -= cost;
            queen.NestsBuilt++;

            if (queen.Health <= 0f)
                KillAgent(queen);

            return true;
        }

        private bool CanConsumeMulch(AntAgent agent, int sameCellCount)
        {
            if (sameCellCount > 1)
                return false;

            AbstractBlock standingOn = World.GetBlock(agent.Cell.x, agent.Cell.y, agent.Cell.z);
            return standingOn is MulchBlock;
        }

        private bool CanDig(AntAgent agent)
        {
            if (agent.IsQueen)
                return false;

            AbstractBlock standingOn = World.GetBlock(agent.Cell.x, agent.Cell.y, agent.Cell.z);
            return
                standingOn.isVisible() &&
                !(standingOn is ContainerBlock) &&
                !(standingOn is NestBlock) &&
                !(standingOn is MulchBlock);
        }

        private bool CanShareHealth(AntAgent donor, int sameCellCount)
        {
            if (donor.IsDead || donor.IsQueen || donor.Health <= 1f)
                return false;

            if (sameCellCount <= 1)
                return false;

            if (QueenAgent != null && !QueenAgent.IsDead && donor.Cell == QueenAgent.Cell && QueenAgent.Health < QueenAgent.MaxHealth)
                return donor.Health > donor.MaxHealth * 0.35f;

            for (int i = 0; i < Agents.Count; i++)
            {
                if (Agents[i].IsDead || ReferenceEquals(Agents[i], donor) || Agents[i].Cell != donor.Cell)
                    continue;

                if (Agents[i].Health < donor.Health - 1f)
                    return true;
            }

            return false;
        }

        private bool CanBuildNest(AntAgent queen)
        {
            if (queen == null || queen.IsDead || !queen.IsQueen)
                return false;

            float cost = queen.MaxHealth * Mathf.Clamp01(Config.Ant_Queen_Nest_Cost_Fraction);
            if (queen.Health < cost)
                return false;

            AbstractBlock standingOn = World.GetBlock(queen.Cell.x, queen.Cell.y, queen.Cell.z);
            if (standingOn is ContainerBlock || standingOn is NestBlock || !standingOn.isVisible())
                return false;

            return true;
        }

        private bool TryMoveDownAfterRemovingSupport(AntAgent agent, int previousY)
        {
            for (int y = previousY - 1; y >= 1; y--)
            {
                if (!World.GetBlock(agent.Cell.x, y, agent.Cell.z).isVisible())
                    continue;

                agent.Cell = new Vector3Int(agent.Cell.x, y, agent.Cell.z);
                return true;
            }

            return false;
        }

        private int GetHeuristicMoveDirection(AntAgent agent, DirectionMoveInfo[] infoBuffer)
        {
            if (QueenAgent == null || QueenAgent.IsDead)
                return -1;

            if (agent.IsQueen)
            {
                if (agent.Health >= agent.MaxHealth * 0.85f)
                    return -1;

                int bestDir = -1;
                float bestScore = float.NegativeInfinity;
                for (int i = 0; i < MoveDirectionCount; i++)
                {
                    if (!infoBuffer[i].IsValid)
                        continue;

                    int distToWorkers = CountWorkersWithinRadius(infoBuffer[i].Cell, 4);
                    float score = distToWorkers;
                    if (infoBuffer[i].Block is AcidicBlock)
                        score -= 3f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestDir = i;
                    }
                }

                return bestDir;
            }

            float workerHealth = agent.MaxHealth > 0f ? agent.Health / agent.MaxHealth : 0f;
            bool queenNeedsHealth = QueenAgent.Health < QueenAgent.MaxHealth * 0.9f;
            int preferred = -1;
            float best = float.NegativeInfinity;

            for (int i = 0; i < MoveDirectionCount; i++)
            {
                if (!infoBuffer[i].IsValid)
                    continue;

                int currentDistance = CellDistance(agent.Cell, QueenAgent.Cell);
                int nextDistance = CellDistance(infoBuffer[i].Cell, QueenAgent.Cell);
                int distanceDelta = currentDistance - nextDistance;

                float score = 0f;
                if (queenNeedsHealth && workerHealth > 0.55f)
                    score += distanceDelta * 2.2f;
                else
                    score -= distanceDelta * 1.1f;

                if (infoBuffer[i].Block is MulchBlock && workerHealth < 0.75f)
                    score += 4f;
                if (infoBuffer[i].Block is AcidicBlock)
                    score -= 5f;

                if (score > best)
                {
                    best = score;
                    preferred = i;
                }
            }

            return preferred;
        }

        private int CountWorkersWithinRadius(Vector3Int cell, int radius)
        {
            int count = 0;
            for (int i = 0; i < Agents.Count; i++)
            {
                if (Agents[i].IsDead || Agents[i].IsQueen)
                    continue;

                if (CellDistance(cell, Agents[i].Cell) <= radius)
                    count++;
            }

            return count;
        }

        private int CellDistance(Vector3Int a, Vector3Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
        }

        private int BuildDirectionMoveInfo(AntAgent agent, DirectionMoveInfo[] infoBuffer)
        {
            int validCount = 0;

            for (int i = 0; i < MoveDirectionCount; i++)
            {
                int nx = agent.Cell.x + MovementDirections[i].x;
                int nz = agent.Cell.z + MovementDirections[i].y;

                DirectionMoveInfo info = new DirectionMoveInfo
                {
                    IsValid = false,
                    Cell = agent.Cell,
                    Block = null,
                };

                if (nx > 0 && nz > 0 && nx < World.WorldSizeX - 1 && nz < World.WorldSizeZ - 1)
                {
                    int ny = FindTopSolidY(nx, nz);
                    if (ny >= 1 && Mathf.Abs(ny - agent.Cell.y) <= 2)
                    {
                        AbstractBlock destination = World.GetBlock(nx, ny, nz);
                        if (!(destination is ContainerBlock))
                        {
                            info.IsValid = true;
                            info.Cell = new Vector3Int(nx, ny, nz);
                            info.Block = destination;
                            validCount++;
                        }
                    }
                }

                infoBuffer[i] = info;
            }

            return validCount;
        }

        private void BuildObservation(
            AntAgent agent,
            int sameCellCount,
            int validMoveCount,
            bool canEat,
            bool canDig,
            bool canShare,
            bool canBuild,
            DirectionMoveInfo[] directionInfo,
            float[] observation)
        {
            Array.Clear(observation, 0, observation.Length);

            float healthRatio = agent.MaxHealth > 0f ? agent.Health / agent.MaxHealth : 0f;
            AbstractBlock standingOn = World.GetBlock(agent.Cell.x, agent.Cell.y, agent.Cell.z);

            observation[0] = Mathf.Clamp01(healthRatio);
            observation[1] = 1f - observation[0];
            observation[2] = agent.IsQueen ? 1f : 0f;
            observation[3] = Mathf.Clamp01((sameCellCount - 1) / 4f);
            observation[4] = standingOn is MulchBlock ? 1f : 0f;
            observation[5] = standingOn is AcidicBlock ? 1f : 0f;
            observation[6] = standingOn is NestBlock ? 1f : 0f;
            observation[7] = (standingOn.isVisible() && !(standingOn is MulchBlock) && !(standingOn is AcidicBlock) && !(standingOn is NestBlock)) ? 1f : 0f;
            observation[8] = canEat ? 1f : 0f;
            observation[9] = canDig ? 1f : 0f;
            observation[10] = canShare ? 1f : 0f;
            observation[11] = canBuild ? 1f : 0f;
            observation[12] = validMoveCount / 4f;

            if (!agent.IsQueen && QueenAgent != null && !QueenAgent.IsDead)
            {
                int dx = QueenAgent.Cell.x - agent.Cell.x;
                int dz = QueenAgent.Cell.z - agent.Cell.z;
                int dist = Mathf.Abs(dx) + Mathf.Abs(dz) + Mathf.Abs(QueenAgent.Cell.y - agent.Cell.y);
                observation[13] = Mathf.Clamp01(dist / 30f);
                observation[14] = Mathf.Clamp(dx / 10f, -1f, 1f);
                observation[15] = Mathf.Clamp(dz / 10f, -1f, 1f);
            }

            for (int i = 0; i < MoveDirectionCount; i++)
            {
                if (!directionInfo[i].IsValid)
                    continue;

                float heightDelta = directionInfo[i].Cell.y - agent.Cell.y;
                observation[16 + i] = Mathf.Clamp(heightDelta / 2f, -1f, 1f);
                observation[20 + i] = directionInfo[i].Block is MulchBlock ? 1f : 0f;
            }
        }

        private void EvaluateNetwork(NeuralGenome genome, float[] inputs, float[] hidden, float[] outputs)
        {
            int p = 0;

            for (int h = 0; h < HiddenSize; h++)
            {
                float sum = 0f;
                for (int i = 0; i < ObservationSize; i++)
                    sum += inputs[i] * genome.Parameters[p++];

                hidden[h] = sum;
            }

            for (int h = 0; h < HiddenSize; h++)
                hidden[h] = (float)Math.Tanh(hidden[h] + genome.Parameters[p++]);

            for (int o = 0; o < NetworkOutputSize; o++)
            {
                float sum = 0f;
                for (int h = 0; h < HiddenSize; h++)
                    sum += hidden[h] * genome.Parameters[p++];

                outputs[o] = sum;
            }

            for (int o = 0; o < NetworkOutputSize; o++)
                outputs[o] += genome.Parameters[p++];
        }

        private int SampleMaskedLogits(float[] logits, bool[] validMask, int count, float temperature)
        {
            float invTemp = 1f / Mathf.Max(0.05f, temperature);
            float maxLogit = float.NegativeInfinity;
            bool hasValid = false;

            for (int i = 0; i < count; i++)
            {
                if (!validMask[i])
                    continue;

                hasValid = true;
                float value = logits[i] * invTemp;
                if (value > maxLogit)
                    maxLogit = value;
            }

            if (!hasValid)
                return (int)AntAction.Idle;

            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                if (!validMask[i])
                    continue;

                total += Mathf.Exp((logits[i] * invTemp) - maxLogit);
            }

            float roll = Range(0f, total);
            for (int i = 0; i < count; i++)
            {
                if (!validMask[i])
                    continue;

                roll -= Mathf.Exp((logits[i] * invTemp) - maxLogit);
                if (roll <= 0f)
                    return i;
            }

            for (int i = count - 1; i >= 0; i--)
            {
                if (validMask[i])
                    return i;
            }

            return (int)AntAction.Idle;
        }

        private int SampleDirection(float[] logits, int offset, DirectionMoveInfo[] infoBuffer, float temperature)
        {
            float invTemp = 1f / Mathf.Max(0.05f, temperature);
            float maxLogit = float.NegativeInfinity;
            bool hasValid = false;

            for (int i = 0; i < MoveDirectionCount; i++)
            {
                if (!infoBuffer[i].IsValid)
                    continue;

                hasValid = true;
                float value = logits[offset + i] * invTemp;
                if (value > maxLogit)
                    maxLogit = value;
            }

            if (!hasValid)
                return 0;

            float total = 0f;
            for (int i = 0; i < MoveDirectionCount; i++)
            {
                if (!infoBuffer[i].IsValid)
                    continue;

                total += Mathf.Exp((logits[offset + i] * invTemp) - maxLogit);
            }

            float roll = Range(0f, total);
            for (int i = 0; i < MoveDirectionCount; i++)
            {
                if (!infoBuffer[i].IsValid)
                    continue;

                roll -= Mathf.Exp((logits[offset + i] * invTemp) - maxLogit);
                if (roll <= 0f)
                    return i;
            }

            for (int i = MoveDirectionCount - 1; i >= 0; i--)
            {
                if (infoBuffer[i].IsValid)
                    return i;
            }

            return 0;
        }

        private float CalculateFitness(AntAgent agent, int queenNests)
        {
            if (agent.IsQueen)
            {
                return
                    (agent.NestsBuilt * 95f) +
                    (agent.StepsAlive * 0.08f) +
                    (agent.Health * 0.35f);
            }

            return
                (agent.MulchConsumed * 4f) +
                (agent.HealthShared * 6f) +
                (agent.StepsAlive * 0.05f) +
                (queenNests * 4f) -
                (agent.BlocksDug * 0.45f);
        }

        private NeuralGenome CreateRandomGenome()
        {
            NeuralGenome genome = new NeuralGenome();
            for (int i = 0; i < genome.Parameters.Length; i++)
                genome.Parameters[i] = Range(-1f, 1f);

            return genome;
        }

        private NeuralGenome MutateGenome(NeuralGenome baseGenome, float strength)
        {
            NeuralGenome mutated = baseGenome.Clone();
            float sigma = Mathf.Max(0.01f, strength);

            for (int i = 0; i < mutated.Parameters.Length; i++)
            {
                if (RNG.NextDouble() < 0.14)
                    mutated.Parameters[i] += (float)NextGaussian() * sigma;

                mutated.Parameters[i] += (float)NextGaussian() * sigma * 0.015f;
                mutated.Parameters[i] = Mathf.Clamp(mutated.Parameters[i], -4f, 4f);
            }

            return mutated;
        }

        private double NextGaussian()
        {
            double u1 = Math.Max(1e-9, RNG.NextDouble());
            double u2 = RNG.NextDouble();
            double radius = Math.Sqrt(-2.0 * Math.Log(u1));
            double theta = 2.0 * Math.PI * u2;
            return radius * Math.Cos(theta);
        }

        private float Range(float min, float max)
        {
            return min + (float)RNG.NextDouble() * (max - min);
        }

        private int CountAliveAgents()
        {
            int alive = 0;
            for (int i = 0; i < Agents.Count; i++)
            {
                if (!Agents[i].IsDead)
                    alive++;
            }

            return alive;
        }

        private int CountLivingAgentsAtCell(Vector3Int cell)
        {
            int count = 0;
            for (int i = 0; i < Agents.Count; i++)
            {
                if (!Agents[i].IsDead && Agents[i].Cell == cell)
                    count++;
            }

            return count;
        }

        private void BuildLiveActionOrder()
        {
            LiveActionOrder.Clear();
            for (int i = 0; i < Agents.Count; i++)
            {
                if (!Agents[i].IsDead)
                    LiveActionOrder.Add(Agents[i]);
            }

            for (int i = LiveActionOrder.Count - 1; i > 0; i--)
            {
                int swapIndex = RNG.Next(0, i + 1);
                AntAgent temp = LiveActionOrder[i];
                LiveActionOrder[i] = LiveActionOrder[swapIndex];
                LiveActionOrder[swapIndex] = temp;
            }
        }

        private void KillAgent(AntAgent agent)
        {
            if (agent.IsDead)
                return;

            agent.IsDead = true;
            agent.Health = 0f;
            if (agent.Visual != null)
                Destroy(agent.Visual);
        }

        private void UpdateVisualPosition(AntAgent agent)
        {
            if (agent.Visual == null)
                return;

            float yOffset = agent.IsQueen ? 0.85f : 0.58f;
            agent.Visual.transform.position = new Vector3(agent.Cell.x, agent.Cell.y + yOffset, agent.Cell.z);
        }

        private int FindTopSolidY(int x, int z)
        {
            for (int y = World.WorldSizeY - 1; y >= 1; y--)
            {
                if (World.GetBlock(x, y, z).isVisible())
                    return y;
            }

            return -1;
        }

        private bool TryFindSpawnNear(Vector3Int anchor, HashSet<Vector3Int> occupied, int radius, out Vector3Int spawnCell)
        {
            int attempts = 140;
            for (int i = 0; i < attempts; i++)
            {
                int x = Mathf.Clamp(anchor.x + RNG.Next(-radius, radius + 1), 1, World.WorldSizeX - 2);
                int z = Mathf.Clamp(anchor.z + RNG.Next(-radius, radius + 1), 1, World.WorldSizeZ - 2);
                int y = FindTopSolidY(x, z);
                if (y < 1)
                    continue;

                Vector3Int candidate = new Vector3Int(x, y, z);
                if (occupied.Contains(candidate))
                    continue;

                AbstractBlock standingOn = World.GetBlock(x, y, z);
                if (standingOn is ContainerBlock)
                    continue;

                spawnCell = candidate;
                return true;
            }

            spawnCell = default;
            return false;
        }

        private bool TryFindSpawnCell(HashSet<Vector3Int> occupied, out Vector3Int spawnCell)
        {
            int attempts = 2400;
            for (int i = 0; i < attempts; i++)
            {
                int x = RNG.Next(1, World.WorldSizeX - 1);
                int z = RNG.Next(1, World.WorldSizeZ - 1);
                int y = FindTopSolidY(x, z);
                if (y < 1)
                    continue;

                Vector3Int candidate = new Vector3Int(x, y, z);
                if (occupied.Contains(candidate))
                    continue;

                AbstractBlock standingOn = World.GetBlock(x, y, z);
                if (standingOn is ContainerBlock)
                    continue;

                spawnCell = candidate;
                return true;
            }

            for (int x = 1; x < World.WorldSizeX - 1; x++)
            {
                for (int z = 1; z < World.WorldSizeZ - 1; z++)
                {
                    int y = FindTopSolidY(x, z);
                    if (y < 1)
                        continue;

                    Vector3Int candidate = new Vector3Int(x, y, z);
                    if (occupied.Contains(candidate))
                        continue;

                    AbstractBlock standingOn = World.GetBlock(x, y, z);
                    if (standingOn is ContainerBlock)
                        continue;

                    spawnCell = candidate;
                    return true;
                }
            }

            int centerX = Mathf.Clamp(World.WorldSizeX / 2, 1, World.WorldSizeX - 2);
            int centerZ = Mathf.Clamp(World.WorldSizeZ / 2, 1, World.WorldSizeZ - 2);
            int centerY = Mathf.Max(1, FindTopSolidY(centerX, centerZ));

            for (int radius = 0; radius < 8; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        int x = Mathf.Clamp(centerX + dx, 1, World.WorldSizeX - 2);
                        int z = Mathf.Clamp(centerZ + dz, 1, World.WorldSizeZ - 2);
                        int y = Mathf.Max(1, FindTopSolidY(x, z));
                        Vector3Int candidate = new Vector3Int(x, y, z);

                        if (occupied.Contains(candidate))
                            continue;

                        AbstractBlock block = World.GetBlock(x, y, z);
                        if (!block.isVisible() || block is ContainerBlock)
                            World.SetBlock(x, y, z, new GrassBlock());

                        spawnCell = candidate;
                        Debug.LogWarning("Spawn fallback was used to place an ant at " + candidate + ".");
                        return true;
                    }
                }
            }

            spawnCell = default;
            return false;
        }
    }
}
