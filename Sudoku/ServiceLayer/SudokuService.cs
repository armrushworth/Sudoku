﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Sudoku.Models;

namespace Sudoku.ServiceLayer
{
    public class SudokuService : ISudokuService
    {
        private const int MaximumSteps = 10000;

        private readonly Dictionary<int, Dictionary<string, int>> _difficultySettings =
            new Dictionary<int, Dictionary<string, int>>
            {
                {4, new Dictionary<string, int> {{"Easy", 6}, {"Medium", 8}, {"Hard", 10}, {"Expert", 12}}},
                {6, new Dictionary<string, int> {{"Easy", 15}, {"Medium", 19}, {"Hard", 23}, {"Expert", 27}}},
                {8, new Dictionary<string, int> {{"Easy", 30}, {"Medium", 36}, {"Hard", 42}, {"Expert", 48}}},
                {9, new Dictionary<string, int> {{"Easy", 33}, {"Medium", 42}, {"Hard", 51}, {"Expert", 60}}},
                {10, new Dictionary<string, int> {{"Easy", 41}, {"Medium", 51}, {"Hard", 61}, {"Expert", 71}}},
                {12, new Dictionary<string, int> {{"Easy", 62}, {"Medium", 74}, {"Hard", 86}, {"Expert", 98}}},
                {14, new Dictionary<string, int> {{"Easy", 75}, {"Medium", 89}, {"Hard", 103}, {"Expert", 117}}},
                {15, new Dictionary<string, int> {{"Easy", 88}, {"Medium", 103}, {"Hard", 118}, {"Expert", 133}}},
                {16, new Dictionary<string, int> {{"Easy", 103}, {"Medium", 119}, {"Hard", 135}, {"Expert", 151}}}
            };

        private List<Cell>[]
            _bucketList; // Keeps cell counts in buckets, allowing the cell with the least candidates to be selected

        // True values for row, grid, and region constraint matrices means that they contain that candidate
        // Inversely, true values in the cell constraint matrix means that it is a possible value for that cell
        private Candidate[,] _cellConstraintMatrix;
        private Stack<List<Cell>> _changed; // Tracks the cells changed due to propagation
        private Candidate[] _colConstraintMatrix;
        private Grid _grid; // Actual puzzle grid
        private Candidate[,] _regionConstraintMatrix;
        private Candidate[] _rowConstraintMatrix;
        private List<List<Cell>> _solutions;
        private int _steps; // Tracks the number of steps a solution takes
        private List<Cell> _unsolved; // Helps avoid iterating over solved squares

        public Grid SetupGrid(int size, string mode)
        {
            var regionSize = CalculateRegionSize(size);

            var cells = new List<Cell>();
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var cell = new Cell
                {
                    Coordinates = new Point(x, y),
                    Region = new Point(x / regionSize[0], y / regionSize[1]),
                    Editable = true
                };
                cells.Add(cell);
            }

            var grid = new Grid
            {
                Size = size,
                RegionWidth = regionSize[0],
                RegionHeight = regionSize[1],
                Cells = cells
            };
            return grid;
        }

        public Grid GenerateSudoku(Grid grid, string difficulty)
        {
            var removedCellsCount = 0;

            _grid = grid;
            _solutions = new List<List<Cell>>();
            _unsolved = new List<Cell>();
            _changed = new Stack<List<Cell>>();
            _bucketList = new List<Cell>[grid.Size + 1];

            for (var i = 0; i <= _grid.Size; i++) _bucketList[i] = new List<Cell>();

            InitializeConstraints();
            InitializeMatrices();
            PopulateCandidates();

            _steps = 1;
            BacktrackingAlgorithm(NextCell(), false);
            foreach (var cell in _grid.Cells)
            {
                cell.Solution = cell.Value ?? 0;
                cell.Editable = false;
            }

            var random = new Random();
            foreach (var i in Enumerable.Range(0, _grid.Cells.Count).OrderBy(x => random.Next()))
            {
                var tmp = _grid.Cells.ConvertAll(cell => new Cell
                {
                    Coordinates = cell.Coordinates,
                    Region = cell.Region,
                    Value = cell.Value,
                    Solution = cell.Solution,
                    Editable = cell.Editable
                });

                _steps = 1;
                _grid.Cells[i].Value = null;
                _solutions = new List<List<Cell>>();
                _unsolved = new List<Cell>();
                _changed = new Stack<List<Cell>>();
                _bucketList = new List<Cell>[grid.Size + 1];

                for (var j = 0; j <= _grid.Size; j++) _bucketList[j] = new List<Cell>();

                InitializeConstraints();
                InitializeMatrices();
                PopulateCandidates();

                if (!BacktrackingAlgorithm(NextCell(), true) && _solutions.Count == 1)
                {
                    _grid.Cells = tmp;
                    _grid.Cells[i].Value = null;
                    _grid.Cells[i].Editable = true;
                    removedCellsCount++;
                }
                else
                {
                    _grid.Cells = tmp;
                }

                if (_steps >= MaximumSteps || removedCellsCount >= _difficultySettings[_grid.Size][difficulty]) break;
            }

            return _grid;
        }

        public Grid SolveSudoku(Grid grid, int?[] sudoku)
        {
            _grid = grid;
            _solutions = new List<List<Cell>>();
            _unsolved = new List<Cell>();
            _changed = new Stack<List<Cell>>();
            _bucketList = new List<Cell>[grid.Size + 1];

            for (var i = 0; i < sudoku.Length; i++)
            {
                _grid.Cells[i].Solution = _grid.Cells[i].Value = sudoku[i];
                _grid.Cells[i].Editable = sudoku[i] != null;
            }

            for (var i = 0; i <= _grid.Size; i++) _bucketList[i] = new List<Cell>();

            InitializeConstraints();
            InitializeMatrices();
            PopulateCandidates();

            _steps = 1;
            _grid.Solved = BacktrackingAlgorithm(NextCell(), false);

            if (_grid.Solved == false)
                for (var i = 0; i < sudoku.Length; i++)
                    _grid.Cells[i].Editable = true;

            return _grid;
        }

        public Grid SubmitSolution(Grid grid, int?[] sudoku)
        {
            grid.Solved = true;
            for (var i = 0; i < grid.Cells.Count; i++)
            {
                grid.Cells[i].Value = sudoku[i];
                if (grid.Cells[i].Solution != sudoku[i]) grid.Solved = false;
            }

            return grid;
        }

        private int[] CalculateRegionSize(int size)
        {
            var width = 0;
            var height = 0;
            var sqrt = Math.Sqrt(size);
            if (sqrt % 1 == 0)
                width = height = (int) sqrt;
            else
                for (var i = 1; i < size; i++)
                {
                    var j = (double) size / i;
                    if (j == Math.Floor(j))
                    {
                        width = i;
                        height = (int) j;
                        if (j <= i) break;
                    }
                }

            return new[] {width, height};
        }

        private void InitializeConstraints()
        {
            _cellConstraintMatrix = new Candidate[_grid.Size, _grid.Size];
            _rowConstraintMatrix = new Candidate[_grid.Size];
            _colConstraintMatrix = new Candidate[_grid.Size];
            _regionConstraintMatrix = new Candidate[_grid.RegionHeight, _grid.RegionWidth];

            for (var i = 0; i < _grid.Size; i++)
            {
                for (var j = 0; j < _grid.Size; j++)
                {
                    _cellConstraintMatrix[i, j] = new Candidate(_grid.Size, true);
                    if (i % _grid.RegionWidth == 0 && j % _grid.RegionHeight == 0)
                        _regionConstraintMatrix[i / _grid.RegionWidth, j / _grid.RegionHeight] =
                            new Candidate(_grid.Size, false);
                }

                _rowConstraintMatrix[i] = new Candidate(_grid.Size, false);
                _colConstraintMatrix[i] = new Candidate(_grid.Size, false);
            }
        }

        private void InitializeMatrices()
        {
            foreach (var cell in _grid.Cells)
                // If the square is solved update the candidate list for the row, column, and region
                if (cell.Value != null)
                {
                    var candidate = cell.Value ?? 0;
                    _rowConstraintMatrix[cell.Coordinates.Y][candidate] = true;
                    _colConstraintMatrix[cell.Coordinates.X][candidate] = true;
                    _regionConstraintMatrix[cell.Region.X, cell.Region.Y][candidate] = true;
                }
        }

        private void PopulateCandidates()
        {
            // Add possible candidates by checking the rows, columns and grid
            foreach (var cell in _grid.Cells)
                // If solved, then there are no possible candidates
                if (cell.Value != null)
                {
                    _cellConstraintMatrix[cell.Coordinates.X, cell.Coordinates.Y].SetAll(false);
                }
                else
                {
                    // Populate each cell with possible candidates by checking the row, col, and grid associated with that cell
                    foreach (int candidate in _rowConstraintMatrix[cell.Coordinates.Y])
                        _cellConstraintMatrix[cell.Coordinates.X, cell.Coordinates.Y][candidate] = false;
                    foreach (int candidate in _colConstraintMatrix[cell.Coordinates.X])
                        _cellConstraintMatrix[cell.Coordinates.X, cell.Coordinates.Y][candidate] = false;
                    foreach (int candidate in _regionConstraintMatrix[cell.Region.X, cell.Region.Y])
                        _cellConstraintMatrix[cell.Coordinates.X, cell.Coordinates.Y][candidate] = false;

                    _bucketList[_cellConstraintMatrix[cell.Coordinates.X, cell.Coordinates.Y].Count].Add(cell);
                    _unsolved.Add(cell);
                }
        }

        private Cell NextCell()
        {
            return _unsolved.Count == 0
                ? null
                : (from cells in _bucketList where cells.Count > 0 select cells.First()).FirstOrDefault();
        }

        private void SelectCandidate(Cell cell, int candidate)
        {
            var changedCells = new List<Cell>();

            // Place candidate on grid
            cell.Value = candidate;

            // Remove from bucket list
            _bucketList[_cellConstraintMatrix[cell.Coordinates.X, cell.Coordinates.Y].Count].Remove(cell);

            // Remove candidate from cell constraint matrix
            _cellConstraintMatrix[cell.Coordinates.X, cell.Coordinates.Y][candidate] = false;

            // Add the candidate to the cell, row, col, region constraint matrices
            _colConstraintMatrix[cell.Coordinates.X][candidate] = true;
            _rowConstraintMatrix[cell.Coordinates.Y][candidate] = true;
            _regionConstraintMatrix[cell.Region.X, cell.Region.Y][candidate] = true;

            // Remove candidates across unsolved cells in the same row and col
            for (var i = 0; i < _grid.Size; i++)
            {
                // Only change unsolved cells containing the candidate
                if (_grid.Cells[i + cell.Coordinates.Y * _grid.Size].Value == null)
                    if (_cellConstraintMatrix[i, cell.Coordinates.Y][candidate])
                    {
                        // Shift affected cells down the bucket list
                        _bucketList[_cellConstraintMatrix[i, cell.Coordinates.Y].Count]
                            .Remove(_grid.Cells[i + cell.Coordinates.Y * _grid.Size]);
                        _bucketList[_cellConstraintMatrix[i, cell.Coordinates.Y].Count - 1]
                            .Add(_grid.Cells[i + cell.Coordinates.Y * _grid.Size]);

                        // Remove the candidate
                        _cellConstraintMatrix[i, cell.Coordinates.Y][candidate] = false;

                        // Update changed cells (for backtracking)
                        changedCells.Add(_grid.Cells[i + cell.Coordinates.Y * _grid.Size]);
                    }

                // Only change unsolved cells containing the candidate
                if (_grid.Cells[cell.Coordinates.X + i * _grid.Size].Value == null)
                    if (_cellConstraintMatrix[cell.Coordinates.X, i][candidate])
                    {
                        // Shift affected cells down the bucket list
                        _bucketList[_cellConstraintMatrix[cell.Coordinates.X, i].Count]
                            .Remove(_grid.Cells[cell.Coordinates.X + i * _grid.Size]);
                        _bucketList[_cellConstraintMatrix[cell.Coordinates.X, i].Count - 1]
                            .Add(_grid.Cells[cell.Coordinates.X + i * _grid.Size]);

                        // Remove the candidate
                        _cellConstraintMatrix[cell.Coordinates.X, i][candidate] = false;

                        // Update changed cells (for backtracking)
                        changedCells.Add(_grid.Cells[cell.Coordinates.X + i * _grid.Size]);
                    }
            }

            // Remove candidates across unsolved cells in the same region
            var gridRowStart = cell.Coordinates.Y / _grid.RegionHeight * _grid.RegionHeight;
            var gridColStart = cell.Coordinates.X / _grid.RegionWidth * _grid.RegionWidth;
            for (var row = gridRowStart; row < gridRowStart + _grid.RegionHeight; row++)
            for (var col = gridColStart; col < gridColStart + _grid.RegionWidth; col++)
                // Only change unsolved cells containing the candidate
                if (_grid.Cells[col + row * _grid.Size].Value == null)
                    if (_cellConstraintMatrix[col, row][candidate])
                    {
                        // Shift affected cells down the bucket list
                        _bucketList[_cellConstraintMatrix[col, row].Count].Remove(_grid.Cells[col + row * _grid.Size]);
                        _bucketList[_cellConstraintMatrix[col, row].Count - 1].Add(_grid.Cells[col + row * _grid.Size]);

                        // Remove the candidate
                        _cellConstraintMatrix[col, row][candidate] = false;

                        // Update changed cells (for backtracking)
                        changedCells.Add(_grid.Cells[col + row * _grid.Size]);
                    }

            // Add cell to solved list
            _unsolved.Remove(cell);
            _changed.Push(changedCells);
        }

        private void UnselectCandidate(Cell cell, int candidate)
        {
            // Remove selected candidate from grid
            cell.Value = null;

            // Add that candidate back to the cell constraint matrix
            _cellConstraintMatrix[cell.Coordinates.X, cell.Coordinates.Y][candidate] = true;

            // Put cell back in the bucket list
            _bucketList[_cellConstraintMatrix[cell.Coordinates.X, cell.Coordinates.Y].Count].Add(cell);

            // Remove the candidate from the row, col, and region constraint matrices
            _rowConstraintMatrix[cell.Coordinates.Y][candidate] = false;
            _colConstraintMatrix[cell.Coordinates.X][candidate] = false;
            _regionConstraintMatrix[cell.Region.X, cell.Region.Y][candidate] = false;

            // Add the candidate back to any cells that changed from its selection
            foreach (var c in _changed.Pop())
            {
                // Shift affected cells up the bucket list
                _bucketList[_cellConstraintMatrix[c.Coordinates.X, c.Coordinates.Y].Count].Remove(c);
                _bucketList[_cellConstraintMatrix[c.Coordinates.X, c.Coordinates.Y].Count + 1].Add(c);
                _cellConstraintMatrix[c.Coordinates.X, c.Coordinates.Y][candidate] = true;
            }

            // Add the cell back to the list of unsolved
            _unsolved.Add(cell);
        }

        private bool BacktrackingAlgorithm(Cell nextCell, bool checkUniqueness)
        {
            // Stop checking for uniqueness if the defined maximum number of steps has been reached
            if (checkUniqueness && _steps >= MaximumSteps) return false;

            // If there are no more unsolved cells, the puzzle has been solved
            if (nextCell == null)
            {
                if (!checkUniqueness || _solutions.Any()) return true;
                _solutions.Add(_grid.Cells.ConvertAll(cell => new Cell
                {
                    Coordinates = cell.Coordinates,
                    Region = cell.Region,
                    Value = cell.Value
                }));
                return false;
            }

            // Loop through all candidates in the cell
            foreach (int candidate in _cellConstraintMatrix[nextCell.Coordinates.X, nextCell.Coordinates.Y])
            {
                SelectCandidate(nextCell, candidate);

                // Move to the next cell. If it returns false, backtrack
                if (BacktrackingAlgorithm(NextCell(), checkUniqueness)) return true;
                ++_steps;
                UnselectCandidate(nextCell, candidate);
            }

            // Return false if path is unsolvable
            return false;
        }
    }
}