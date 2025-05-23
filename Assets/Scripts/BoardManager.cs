using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Linq;

public class BoardManager : MonoBehaviour
{
    public static BoardManager Instance { get; private set; }

    public event Action<CellData, Faction, Faction> OnCellOwnershipChanged;

    [Header("Config")]
    [SerializeField] private bool usePrePaintedMap = false;
    [SerializeField] private Grid grid;
    [Tooltip("Offset for rendering units in the middle of a tile")]
    [SerializeField] private Vector2 renderingOffset = new(0.5f, 0.5f);
    
    [Header("Config - Terrain tilemap and tile references")]
    [SerializeField] private Tilemap terrainTilemap;
    [SerializeField] private TileBase neutralTerrainTile;
    [SerializeField] private TileBase catTerrainTile;
    [SerializeField] private TileBase hyenaTerrainTile;

    [Header("Config - Resource tilemap and tile references")]
    [SerializeField] private Tilemap resourceTilemap;
    [SerializeField] private TileBase[] resourceTiles;

    [Header("Config - Range tilemap and tile references")]
    [SerializeField] private Tilemap rangeTilemap;
    [SerializeField] private TileBase rangeTile;
    [SerializeField] private TileBase shootingRangeTile;

    [Header("State")]
    [SerializeField] private int width;
    [SerializeField] private int height;
    [SerializeField] private Vector2Int boardCellToGridmapCellOffset;
    [SerializeField] private CellData[,] board;
    [SerializeField] private Dictionary<Vector2Int, Unit> cellToUnitMap = new();

    #region Board Initialization

    private readonly int[,] predefinedBoard = new int[,]
    {
        {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, 1, 1,-1,-1,-1},
        {-1,-1,-1,-1,-1,-1, 1, 1, 1, 1, 2, 1, 2, 1, 1, 1, 1, 1,-1,-1,-1},
        {-1,-1,-1,-1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 3, 3,-1,-1,-1},
        {-1,-1,-1,-1, 1, 4, 1, 1, 1, 1, 1, 2, 1, 2, 3, 2, 4, 2, 1,-1,-1},
        {-1,-1,-1, 1, 1, 1, 1,-1,-1, 2, 1, 2, 2, 2, 1, 2, 1, 1, 1, 3,-1},
        {-1,-1,-1,-1, 1, 3, 1,-1,-1, 2, 1, 1, 2, 2, 1, 2, 1, 1, 1, 2, 1},
        {-1,-1,-1,-1, 3, 2, 1, 1, 1, 1, 1, 2, 2, 3, 2,-1,-1, 2, 3, 2, 1},
        {-1,-1,-1, 1, 1, 1, 1, 1, 1, 2, 2, 1, 4, 2, 3,-1,-1, 2, 1, 1,-1},
        {-1,-1,-1,-1, 1, 1, 2, 1, 1, 2, 1, 1, 3, 1, 2, 1, 1, 2, 1,-1,-1},
        {-1, 2, 1, 2, 2, 3, 2, 1, 1, 2, 2, 1, 1, 2, 1, 1, 3,-1,-1,-1,-1},
        {-1, 3, 1, 1, 2, 1, 1, 1, 1, 2, 1, 1, 2, 1, 1, 1, 2, 1,-1,-1,-1},
        {-1, 3, 2, 1, 1, 1, 1,-1,-1, 2, 3, 2, 1, 2, 2, 3, 2, 1,-1,-1,-1},
        { 1, 1, 1, 1, 1, 1, 2, 2, 1, 2, 2, 4, 1, 1, 2, 1, 1,-1,-1,-1,-1},
        {-1, 1, 1, 2, 1, 1, 2, 1, 1, 3, 1, 2, 1, 1, 2, 1,-1,-1,-1,-1,-1},
        {-1,-1,-1,-1, 1, 1, 2, 2, 1, 1, 2, 1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {-1,-1,-1,-1, 1, 1, 2, 1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1}
    };

    void Awake()
    {
        Instance = this;
    
        if(usePrePaintedMap)
        {
            InitializeBoardFromPaintedTilemap();
        }
        else
        {
            InitializeBoardAndTilemapsFromPredefinedBoard();
        }

        Debug.Log($"Board initialized ===== width: {width}  height: {height}  boardCellToGridmapCellOffset: {boardCellToGridmapCellOffset}");
    }

    private void Start()
    {
        // Check if there is an active HQUnit in the scene, if so, we'll let it call this method when it's finished Starting()
        if (FindAnyObjectByType<HQUnit>() != null)
        {
            Debug.Log("[BoardManager] HQUnit found in scene so will let it trigger minDistToHQ computation.");
            return;
        }

        Vector2Int centerCell = GetCenterCell();
        Debug.LogWarning($"[BoardManager] HQUnit not found in scene. Using center cell at {centerCell} as reference instead.");
        ComputeMinDistToHQ(centerCell);
    }

    private void InitializeBoardFromPaintedTilemap()
    {
        terrainTilemap.CompressBounds();
        BoundsInt bounds = terrainTilemap.cellBounds;
        Debug.Log($"Bounds: {bounds}");

        width = bounds.size.x;
        height = bounds.size.y;
        boardCellToGridmapCellOffset = new Vector2Int(bounds.x, bounds.y);

        board = new CellData[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3Int tilemapPos = new(bounds.x + x, bounds.y + y, 0);

                TileBase terrainTile = terrainTilemap.GetTile(tilemapPos);
                TileBase resourceTile = resourceTilemap.GetTile(tilemapPos);

                CellData cellData;
                if (terrainTile == null)
                {
                    resourceTilemap.SetTile(tilemapPos, null);
                    cellData = new CellData(-1, Faction.None);
                }
                else
                {
                    Faction faction = GetFactionForTerrainTile(terrainTile, tilemapPos);
                    int resourceValue = GetResourceValueForResourceTile(resourceTile, tilemapPos);
                    cellData = new CellData(resourceValue, faction);
                }

                board[x, y] = cellData;
            }
        }
    }

    private void InitializeBoardAndTilemapsFromPredefinedBoard()
    {
        width = predefinedBoard.GetLength(1);
        height = predefinedBoard.GetLength(0);
        boardCellToGridmapCellOffset = new Vector2Int(0, 0);

        board = new CellData[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int resourceValue = predefinedBoard[height - y - 1, x];

                board[x, y] = new CellData(resourceValue);

                if (resourceValue != -1)
                {
                    Vector3Int pos = new(x, y, 0);
                    terrainTilemap.SetTile(pos, GetTerrainTileForFaction(Faction.None));
                    resourceTilemap.SetTile(pos, GetResourceTileForResourceValue(resourceValue));
                }
            }
        }
    }

    private TileBase GetTerrainTileForFaction(Faction faction)
    {
        return faction switch
        {
            Faction.Cats => catTerrainTile,
            Faction.Hyenas => hyenaTerrainTile,
            Faction.None => neutralTerrainTile,
            _ => neutralTerrainTile,
        };
    }

    private Faction GetFactionForTerrainTile(TileBase tile, Vector3Int pos)
    {
        if (tile == neutralTerrainTile) return Faction.None;
        else if (tile == catTerrainTile) return Faction.Cats;
        else if (tile == hyenaTerrainTile) return Faction.Hyenas;
        else if (tile == null)
        {
            Debug.LogError($"No faction for null terrain tile at {pos}");
            return Faction.None;
        }
        else
        {
            Debug.LogError($"Unknown terrain tile at {pos}");
            return Faction.None;
        }
    }

    private TileBase GetResourceTileForResourceValue(int resourceValue)
    {
        return resourceValue switch
        {
            2 => resourceTiles[0],
            3 => resourceTiles[1],
            4 => resourceTiles[2],
            _ => null,
        };
    }

    private int GetResourceValueForResourceTile(TileBase tile, Vector3Int pos)
    {
        if (tile == null) return 1;
        else if (tile == resourceTiles[0]) return 2;
        else if (tile == resourceTiles[1]) return 3;
        else if (tile == resourceTiles[2]) return 4;
        else
        {
            Debug.LogError($"Unknown terrain tile at {pos}");
            return 1;
        }
    }

    public void ComputeMinDistToHQ(Vector2Int hqPos)
    {
        Debug.Log($"[BoardManager] Computing minDistToHQ for all board cells with {hqPos} as reference...");

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        frontier.Enqueue(hqPos);

        board[hqPos.x, hqPos.y].minDistToHQ = 0; // HQ has distance 0 to itself

        Vector2Int[] directions = new Vector2Int[] {Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left};

        // Standard BFS
        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            int currentDist = board[current.x, current.y].minDistToHQ;

            foreach (Vector2Int dir in directions)
            {
                Vector2Int neighbor = current + dir;

                if (IsValidCellForUnitMovement(neighbor) && board[neighbor.x, neighbor.y].minDistToHQ == int.MaxValue)
                {
                    board[neighbor.x, neighbor.y].minDistToHQ = currentDist + 1;
                    frontier.Enqueue(neighbor);
                }
            }
        }

        Debug.Log("[BoardManager] Finished computing minDistToHQ for all board cells.");
    }

    #endregion

    #region Mapping to/from board cell

    public Vector3 BoardCellToWorld(Vector2Int boardCell)
    {
        Vector3 worldPosBottomLeftCorner = grid.CellToWorld(BoardCellToGridmapCell(boardCell));
        return worldPosBottomLeftCorner + (Vector3)renderingOffset; // Positions in the middle of the tile
    }

    public Vector2Int WorldToBoardCell(Vector3 worldPos)
    {
        return GridmapCellToBoardCell(grid.WorldToCell(worldPos));
    }

    public Vector3Int BoardCellToGridmapCell(Vector2Int boardCell)
    {
        return (Vector3Int)(boardCell + boardCellToGridmapCellOffset);
    }

    public Vector2Int GridmapCellToBoardCell(Vector3Int gridmapCell)
    {
        return (Vector2Int)gridmapCell - boardCellToGridmapCellOffset;
    }

    #endregion

    #region Board and cell inspection

    public CellData[,] GetBoard()
    {
        return board;
    }

    public int GetBoardWidth()
    {
        return width;
    }

    public int GetBoardHeight()
    {
        return height;
    }

    public Vector2Int GetCenterCell()
    {
        return new Vector2Int(width / 2, height / 2);
    }

    public CellData GetCellDataOccupiedByUnit(Unit unit)
    {
        if (unit == null)
        {
            return null;
        }

        return GetCellDataAt(unit.GetBoardPosition());
    }

    public CellData GetCellDataAt(Vector2Int cell)
    {
        return IsWithinBoardBounds(cell) ? board[cell.x, cell.y] : null;
    }

    public bool IsValidCellForUnitMovement(Vector2Int cell)
    {
        return IsWithinBoardBounds(cell) && !IsVoidCell(cell);
    }

    public bool IsVoidCell(Vector2Int pos)
    {
        return IsWithinBoardBounds(pos) && board[pos.x, pos.y].IsVoidCell();
    }

    private bool IsWithinBoardBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < width &&
               cell.y >= 0 && cell.y < height;
    }

    public bool IsOwnedBy(Vector2Int cell, Faction faction)
    {
        return IsWithinBoardBounds(cell) && board[cell.x, cell.y].owner == faction;
    }

    public int GetResourceTotalOfCellsOwnedBy(Faction faction)
    {
        int resourceTotal = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (board[x, y].owner == faction)
                {
                    resourceTotal += board[x, y].resourceValue;
                }
            }
        }
        return resourceTotal;
    }

    #endregion

    #region Unit registry

    public bool CellHasUnit(Vector2Int cell)
    {
        return IsWithinBoardBounds(cell) && cellToUnitMap.ContainsKey(cell);
    }

    public Unit GetUnitAt(Vector2Int cell)
    {
        if (cellToUnitMap.ContainsKey(cell))
        {
            return cellToUnitMap[cell];
        }
        return null;
    }

    public bool TryGetUnitAt(Vector2Int cell, out Unit unit)
    {
        if (cellToUnitMap.ContainsKey(cell))
        {
            unit = cellToUnitMap[cell];
            return true;
        }
        unit = null;
        return false;
    }

    public List<Unit> GetAllUnits(Faction faction)
    {
        return cellToUnitMap.Values
            .Where(unit => unit.GetFaction() == faction)
            .ToList();
    }

    public void RegisterUnitInCell(Unit unit, Vector2Int cell)
    {
        Debug.Log($"RegisterUnitInCell {unit.name} at {cell}");
        if (cellToUnitMap.ContainsKey(cell))
        {
            Unit oldUnit = cellToUnitMap[cell];
            if (oldUnit != unit)
            {
                Debug.LogWarning($"An unit is already registered at {cell}. Replacing {oldUnit.name} with {unit.name}.");
            }
        }

        cellToUnitMap[cell] = unit;
    }

    public void UpdateUnitFromOldToNewCell(Unit unit, Vector2Int oldCell, Vector2Int newCell)
    {
        Debug.Log($"UpdateUnitFromOldToNewCell {unit.name} from {oldCell} to {newCell}");
        UnregisterUnitFromCell(unit, oldCell);
        RegisterUnitInCell(unit, newCell);
    }

    public void UnregisterUnitFromCell(Unit unit, Vector2Int cell)
    {
        Debug.Log($"UnregisterUnitFromCell {unit.name} at {cell}");
        if (cellToUnitMap.ContainsKey(cell))
        {
            if (cellToUnitMap[cell] == unit)
            {
                cellToUnitMap.Remove(cell);
            }
            else
            {
                Debug.LogWarning($"Trying to unregister unit {unit.name} from {cell}, but unit {cellToUnitMap[cell].name} it currently registered there.");
            }
        }
        else
        {
            Debug.LogWarning($"Trying to unregister unit {unit.name} from {cell}, but no unit is currently registered there.");
        }
    }

    #endregion

    #region Range tilemap

    public void ShowMovementRange(Mover moveRange)
    {
        ShowRange(moveRange, rangeTile);

        // Also highlight the cell the unit is standing on
        rangeTilemap.SetTile(BoardCellToGridmapCell(moveRange.GetUnitBoardPosition()), rangeTile);
    }

    public void ShowBuildRange(CellRange buildRange)
    {
        ShowRange(buildRange, rangeTile);
    }

    public void ShowShootingRange(CellRange range)
    {
        ShowRange(range, shootingRangeTile);
    }

    private void ShowRange(CellRange range, TileBase tile)
    {
        foreach (var cell in range.GetCellsInRange())
        {
            rangeTilemap.SetTile(BoardCellToGridmapCell(cell), tile);
        }
    }

    public void ClearAllRanges()
    {
        rangeTilemap.ClearAllTiles();
    }

    #endregion

    public void ClaimCellForFaction(Vector2Int cell, Faction newFaction)
    {
        if (IsValidCellForUnitMovement(cell))
        {
            Faction prevFaction = board[cell.x, cell.y].owner;
            board[cell.x, cell.y].owner = newFaction;
            terrainTilemap.SetTile(BoardCellToGridmapCell(cell), GetTerrainTileForFaction(newFaction));
            OnCellOwnershipChanged?.Invoke(board[cell.x, cell.y], prevFaction, newFaction);
        }
    }

    #region Debug

    // For easier debugging of hyena movement algorithm
    private void OnDrawGizmos()
    {
        if (board == null)
            return;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cellInfo = board[x, y];
                if (cellInfo == null || cellInfo.IsVoidCell())
                    continue;

                Vector2Int cell = new Vector2Int(x, y);
                Vector3 cellLabelOffset = new Vector3(0, -0.3f, 0);
                Vector3 worldPos = BoardCellToWorld(cell);

                Handles.Label(worldPos + cellLabelOffset, cell.ToString());
                Handles.Label(worldPos, cellInfo.minDistToHQ.ToString());
            }
        }
    }

    #endregion
}
