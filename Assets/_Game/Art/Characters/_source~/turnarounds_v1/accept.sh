#!/bin/bash
# Copy an accepted generation into turnaround/<character>/<character>-<view>-2k.jpg.  usage: accept.sh <scratch>/gen <character> <view> <genname>
H=$(cd "$(dirname "$0")" && pwd); mkdir -p "$H/turnaround/$2"
sips -s format jpeg -s formatOptions 88 "$1/${4}_2.png" --out "$H/turnaround/$2/$2-$3-2k.jpg" >/dev/null && echo "accepted $2 $3 <- $4"
