﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sudoku.ServiceLayer
{
    internal class Candidate : IEnumerable
    {
        private readonly int _gridSize;
        private readonly bool[] _values;

        public Candidate(int gridSize, bool initialValue)
        {
            _gridSize = gridSize;
            _values = new bool[gridSize];
            Count = 0;

            for (var i = 1; i <= gridSize; i++) this[i] = initialValue;
        }

        public int Count { get; private set; }

        public bool this[int key]
        {
            // Allows candidates to be referenced by their actual value
            get => _values[key - 1];

            // Automatically tracks the number of candidates
            set
            {
                Count += _values[key - 1] == value ? 0 : value ? 1 : -1;
                _values[key - 1] = value;
            }
        }

        public IEnumerator GetEnumerator()
        {
            return new CandidateEnumerator(this);
        }

        public void SetAll(bool value)
        {
            for (var i = 1; i <= _gridSize; i++) this[i] = value;
        }

        public override string ToString()
        {
            var values = new StringBuilder();
            foreach (int candidate in this) values.Append(candidate);

            return values.ToString();
        }

        private class CandidateEnumerator : IEnumerator
        {
            private readonly Candidate _candidate;
            private readonly List<int> _randomize;
            private int _position;

            public CandidateEnumerator(Candidate candidate)
            {
                _candidate = candidate;
                _randomize = new List<int>(Enumerable.Range(1, _candidate._gridSize).OrderBy(x => Guid.NewGuid()));
                _position = 0;
            }

            // Only iterates over valid candidates
            public bool MoveNext()
            {
                _position++;
                return _position <= _candidate._gridSize && (_candidate[_randomize[_position - 1]] || MoveNext());
            }

            public void Reset()
            {
                _position = 0;
            }

            public object Current => _randomize[_position - 1];
        }
    }
}