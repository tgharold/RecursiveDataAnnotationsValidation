#!/usr/bin/env sh
# Run this from the root of the solution, not the benchmark directory.
# See: https://benchmarkdotnet.org/articles/guides/console-args.html
dotnet build --configuration Release && \
  dotnet run --configuration Release --project benchmark/Benchmark -- --filter '*' --memory

