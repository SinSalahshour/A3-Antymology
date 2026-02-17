using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConfigurationManager : Singleton<ConfigurationManager>
{

    /// <summary>
    /// The seed for world generation.
    /// </summary>
    public int Seed = 1337;

    /// <summary>
    /// The number of chunks in the x and z dimension of the world.
    /// </summary>
    public int World_Diameter = 16;

    /// <summary>
    /// The number of chunks in the y dimension of the world.
    /// </summary>
    public int World_Height = 4;

    /// <summary>
    /// The number of blocks in any dimension of a chunk.
    /// </summary>
    public int Chunk_Diameter = 8;

    /// <summary>
    /// How much of the tile map does each tile take up.
    /// </summary>
    public float Tile_Map_Unit_Ratio = 0.25f;

    /// <summary>
    /// The number of acidic regions on the map.
    /// </summary>
    public int Number_Of_Acidic_Regions = 10;

    /// <summary>
    /// The radius of each acidic region
    /// </summary>
    public int Acidic_Region_Radius = 5;

    /// <summary>
    /// The number of acidic regions on the map.
    /// </summary>
    public int Number_Of_Conatiner_Spheres = 5;

    /// <summary>
    /// The radius of each acidic region
    /// </summary>
    public int Conatiner_Sphere_Radius = 20;

    [Header("Ant Simulation")]
    /// <summary>
    /// Number of worker ants per generation (queen is spawned in addition to this count).
    /// </summary>
    public int Ant_Worker_Count = 32;

    /// <summary>
    /// Number of simulation timesteps in each generation.
    /// </summary>
    public int Ant_Evaluation_Steps = 700;

    /// <summary>
    /// Seconds between each ant simulation tick.
    /// </summary>
    public float Ant_Tick_Seconds = 0.15f;

    /// <summary>
    /// Maximum health of a worker ant.
    /// </summary>
    public float Ant_Worker_Max_Health = 24f;

    /// <summary>
    /// Maximum health of the queen ant.
    /// </summary>
    public float Ant_Queen_Max_Health = 48f;

    /// <summary>
    /// Base health reduction applied every simulation tick.
    /// </summary>
    public float Ant_Base_Health_Drain = 0.25f;

    /// <summary>
    /// Health restored when an ant consumes a mulch block.
    /// </summary>
    public float Ant_Mulch_Health_Restore = 12f;

    /// <summary>
    /// Health amount transferred during a health sharing action.
    /// </summary>
    public float Ant_Health_Transfer_Amount = 3f;

    /// <summary>
    /// Fraction of queen max health spent to produce a nest block.
    /// </summary>
    public float Ant_Queen_Nest_Cost_Fraction = 0.33333334f;

    [Header("Evolution")]
    /// <summary>
    /// Number of elite worker genomes retained each generation.
    /// </summary>
    public int Ant_Elite_Count = 4;

    /// <summary>
    /// Strength of genome mutation between generations.
    /// </summary>
    public float Ant_Mutation_Strength = 0.32f;

    /// <summary>
    /// Regenerates terrain at the start of each generation so fitness is measured from a clean world state.
    /// </summary>
    public bool Ant_Reset_World_Each_Generation = true;
}
