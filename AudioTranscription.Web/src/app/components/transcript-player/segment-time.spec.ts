import { findActiveSegment, formatTimestamp } from './segment-time';

const segments = [
  { index: 0, startMs: 0, endMs: 1_500, text: 'a' },
  { index: 1, startMs: 1_500, endMs: 3_000, text: 'b' },
  // gap from 3.0 s to 4.0 s
  { index: 2, startMs: 4_000, endMs: 6_000, text: 'c' },
];

describe('findActiveSegment', () => {
  it.each([
    [0, 0],
    [1_499, 0],
    [1_500, 1],
    [2_999, 1],
    [4_000, 2],
    [5_999, 2],
  ])('maps %i ms to segment %i', (ms, expected) => {
    expect(findActiveSegment(segments, ms)).toBe(expected);
  });

  it.each([[3_000], [3_500], [6_000], [99_000], [-1]])('finds no segment at %i ms (gap, end or before start)', (ms) => {
    expect(findActiveSegment(segments, ms)).toBe(-1);
  });

  it('handles an empty list', () => {
    expect(findActiveSegment([], 1_000)).toBe(-1);
  });
});

describe('formatTimestamp', () => {
  it.each([
    [0, '0:00'],
    [1_999, '0:01'],
    [65_000, '1:05'],
    [3_599_999, '59:59'],
    [3_723_000, '1:02:03'],
  ])('formats %i ms as %s', (ms, expected) => {
    expect(formatTimestamp(ms)).toBe(expected);
  });
});
