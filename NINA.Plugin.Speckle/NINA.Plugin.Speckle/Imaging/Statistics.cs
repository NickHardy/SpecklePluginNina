using System;

namespace NINA.Plugin.Speckle.Imaging {

    public static class Statistics {

        public static void PartitionAroundRank(double[] values, int left, int right, int rank) {
            while (left < right) {
                var pivot = MedianOfThree(values, left, right);
                var lessEnd = left;
                var greaterStart = right;
                var scan = left;
                while (scan <= greaterStart) {
                    var current = values[scan];
                    if (current < pivot) {
                        Swap(values, scan, lessEnd);
                        scan++;
                        lessEnd++;
                    } else if (current > pivot) {
                        Swap(values, scan, greaterStart);
                        greaterStart--;
                    } else {
                        scan++;
                    }
                }
                if (rank < lessEnd) {
                    right = lessEnd - 1;
                } else if (rank > greaterStart) {
                    left = greaterStart + 1;
                } else {
                    return;
                }
            }
        }

        public static double Median(double[] values, int offset, int count) {
            if (count <= 0) {
                return 0.0;
            }
            if (count == 1) {
                return values[offset];
            }
            var right = offset + count - 1;
            var upperIndex = offset + count / 2;
            PartitionAroundRank(values, offset, right, upperIndex);
            var upper = values[upperIndex];
            if ((count & 1) == 1) {
                return upper;
            }
            PartitionAroundRank(values, offset, upperIndex - 1, upperIndex - 1);
            return 0.5 * (values[upperIndex - 1] + upper);
        }

        public static void Percentiles(double[] scratch, int count, double lowPercent, double highPercent,
                                       out double low, out double high) {
            low = 0.0;
            high = 0.0;
            if (count <= 0) {
                return;
            }
            if (count == 1) {
                low = scratch[0];
                high = scratch[0];
                return;
            }

            var lowPosition = lowPercent / 100.0 * (count - 1);
            var highPosition = highPercent / 100.0 * (count - 1);
            var lowIndex = (int)Math.Floor(lowPosition);
            var highIndex = (int)Math.Floor(highPosition);
            lowIndex = Math.Clamp(lowIndex, 0, count - 1);
            highIndex = Math.Clamp(highIndex, 0, count - 1);
            var lowFraction = lowPosition - lowIndex;
            var highFraction = highPosition - highIndex;

            Span<int> wanted = stackalloc int[4];
            var wantedCount = 0;
            wanted[wantedCount++] = lowIndex;
            if (lowFraction > 0.0 && lowIndex + 1 < count) {
                wanted[wantedCount++] = lowIndex + 1;
            }
            AppendIfNew(wanted, ref wantedCount, highIndex);
            if (highFraction > 0.0 && highIndex + 1 < count) {
                AppendIfNew(wanted, ref wantedCount, highIndex + 1);
            }

            var left = 0;
            for (var i = 0; i < wantedCount; i++) {
                var wantedRank = wanted[i];
                if (wantedRank < left) {
                    continue;
                }
                PartitionAroundRank(scratch, left, count - 1, wantedRank);
                left = wantedRank + 1;
            }

            low = Interpolate(scratch, count, lowIndex, lowFraction);
            high = Interpolate(scratch, count, highIndex, highFraction);
        }

        private static void AppendIfNew(Span<int> wanted, ref int wantedCount, int value) {
            if (wantedCount > 0 && wanted[wantedCount - 1] >= value) {
                return;
            }
            wanted[wantedCount++] = value;
        }

        private static double Interpolate(double[] sorted, int count, int index, double fraction) {
            if (fraction <= 0.0 || index + 1 >= count) {
                return sorted[index];
            }
            return sorted[index] + fraction * (sorted[index + 1] - sorted[index]);
        }

        private static double MedianOfThree(double[] values, int left, int right) {
            var mid = left + ((right - left) >> 1);
            var first = values[left];
            var middle = values[mid];
            var last = values[right];
            if (first > middle) {
                var swap = first;
                first = middle;
                middle = swap;
            }
            if (middle > last) {
                middle = last;
            }
            return first > middle ? first : middle;
        }

        private static void Swap(double[] values, int i, int j) {
            var swap = values[i];
            values[i] = values[j];
            values[j] = swap;
        }
    }
}
