using Godot;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace VoxelPlugin {
public partial class Chunk
{
    private static ConcurrentDictionary<Vector3I, Chunk> chunkList = new ConcurrentDictionary<Vector3I, Chunk>();

    public static Vector3I SIZE = new Vector3I(16,256,16);
    public Block[,,] grid;

    public bool automaticUpdating = false;
    public bool generating = true;
    public bool hasRenderer = false;
    public VoxelRenderer voxelRenderer;
    public Node3D world;
    public Vector3 position;

    public IGenerator generator;

    public void Prepare() {
        CreateVoxelGrid();
        chunkList.TryAdd(PositionToChunkCoord(position), this);

        generator = new NoiseLayer("Stone",30f);
        ((NoiseLayer) generator).AddLayer("Dirt");
        ((NoiseLayer) generator).AddLayer("Dirt");
        ((NoiseLayer) generator).AddLayer("Dirt");
        ((NoiseLayer) generator).AddLayer("Grass");
        generator.Generate(this);

        generator = new NoiseCaves(1f);
        generator.Generate(this);

        automaticUpdating = true;
        generating = false;
        InitBlockSides();
        Update(false);
        UpdateSurroundingChunks();
    }

    public void Remove() {
        chunkList.TryRemove(PositionToChunkCoord(position), out _);
        DeleteVoxelRenderer();
    }

    public void DeleteVoxelRenderer() {
        hasRenderer = false;
        if(voxelRenderer != null) {
            voxelRenderer.chunk = null;
            VoxelMain.ReturnRenderer(voxelRenderer);
        }
        voxelRenderer = null;
    }


    public void CreateVoxelGrid() {
        grid = new Block[SIZE.X,SIZE.Y,SIZE.Z];
        BlockType air = BlockLibrary.GetBlockType("Air");

        for(int x = 0; x < SIZE.X; x++) {
            for(int y = 0; y < SIZE.Y; y++) {
                for(int z = 0; z < SIZE.Z; z++) {
                    Block block = new Block(this);
                    block.position = new Vector3(x,y,z) + position;
                    block.SetBlockType(air);
                    grid[x,y,z] = block;
                }
            }
        }
    }

    private void InitBlockSides() {
        for(int x = 0; x < SIZE.X; x++) {
            for(int y = 0; y < SIZE.Y; y++) {
                for(int z = 0; z < SIZE.Z; z++) {
                    Block block = grid[x,y,z];
                    
                    if(x == 0 || y == 0 || z == 0
                    || x == SIZE.X-1 || y == SIZE.Y-1 || z == SIZE.Z-1) block.UpdateSurroundingBlocks();
                    block.UpdateSelf();
                }
            }
        }
    }

    public void Update(bool fromBlock = true) {
        if(!automaticUpdating) return;
        CreateVoxelRenderer();
        voxelRenderer?.RequestUpdate(grid, fromBlock);
    }

    static Vector3[] neighbours = new Vector3[] {
		Vector3.Up,
		Vector3.Down,
		Vector3.Left,
		Vector3.Right,
		Vector3.Forward,
		Vector3.Back
	};

    public void UpdateSurroundingChunks(bool fromBlock = false) {
        for(int i = 0; i < neighbours.Length; i++) {
			Chunk.GetChunk(position + neighbours[i]*SIZE)?.Update(fromBlock);
		}
    }

    private void CreateVoxelRenderer() {
        if(hasRenderer) return;
        hasRenderer = true;
        world.CallDeferred("CreateVoxelRenderer", position);        
    }

    public static Chunk GetChunk(Vector3 position) {
        Vector3I coord = PositionToChunkCoord(position);
        if(chunkList.ContainsKey(coord)) {
            Chunk chunk = chunkList[coord];
            return chunk;
        }
        return null;
    }

    public static BlockType GetBlockType(Vector3 position) {
        Block block = GetBlock(position);
        if(block == null) return null;
        return block.blockType;
    }

    public static BlockType GetBlockType(Chunk chunk, Vector3 position) {
        Block block = GetBlock(chunk, position);
        if(block == null) return null;
        return block.blockType;
    }

    public static Block GetBlock(Vector3 position) {
        Vector3I chunkCoord = PositionToChunkCoord(position);
        
        if(chunkList.ContainsKey(chunkCoord)) {
            Chunk chunk = chunkList[chunkCoord];
            return chunk.GetBlockLocal(position); 
        }
        
        return null;
    }

    public static Block GetBlock(Chunk chunk, Vector3 position) {
        Block block = chunk?.GetBlockLocal(position);
        if(block != null) return block;
        return GetBlock(position);
    }

    public Block GetRandomBlock(RandomNumberGenerator rng) {
        return grid[rng.RandiRange(0,SIZE.X-1),rng.RandiRange(0,SIZE.Y-1),rng.RandiRange(0,SIZE.Z-1)];
    }

    public static bool SetBlock(Vector3 position, BlockType blockType, int priority = -1) {
        Block block = GetBlock(position);
        if(block != null) {
            block.SetBlockType(blockType, priority);
            return true;
        }

        return false;
    }

    public static bool SetBlock(Chunk chunk, Vector3 position, BlockType blockType, int priority = -1) {
        Block block = chunk.GetBlockLocal(position);
        if(block != null) {
            block.SetBlockType(blockType, priority);
            return true;
        }

        return SetBlock(position, blockType, priority);
    }

    public static void BulkSet(List<Vector3> positions, BlockType blockType, int priority = -1) {
        List<Chunk> affectedChunks = new List<Chunk>();

        for(int i = 0; i < positions.Count; i++) {
            Vector3 position = positions[i];
            Chunk chunk = GetChunk(position);
            if(chunk == null) continue;
            Block block = GetBlock(chunk, position);
            if(block == null) continue;
            affectedChunks.Add(chunk);
            block.SetBlockType(blockType, priority, false);
        }

        foreach(Chunk chunk in affectedChunks) {
            chunk.Update();
            chunk.UpdateSurroundingChunks(true);
        }
    }

    private Block GetBlockLocal(Vector3 position) {
        Vector3I blockCoord = PositionToCoord(position);
        if(blockCoord.X >= 0
            && blockCoord.Y >= 0
            && blockCoord.Z >= 0
            && blockCoord.X < SIZE.X
            && blockCoord.Y < SIZE.Y
            && blockCoord.Z < SIZE.Z) return grid[blockCoord.X, blockCoord.Y, blockCoord.Z];
        return null;
    }

    public Vector3I PositionToCoord(Vector3 position) {
        return Vector3ToVector3I(position - this.position);
    }

    public static Vector3I PositionToChunkCoord(Vector3 position) {
        return Vector3ToVector3I(position/SIZE);
    }

    public static Vector3I Vector3ToVector3I(Vector3 vector) {
        return new Vector3I(Mathf.FloorToInt(vector.X), Mathf.FloorToInt(vector.Y), Mathf.FloorToInt(vector.Z));
    }

    public static Vector3 Vector3IToVector3(Vector3I vector) {
        return new Vector3(vector.X, vector.Y, vector.Z);
    }
}
}
