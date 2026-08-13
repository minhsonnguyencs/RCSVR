using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* pathfinding over the directed Lane graph.
/// Current routes use free-flow travel time only. The cost call is routed
/// through RoadNetworkManager so congestion can be enabled later without
/// changing the A* implementation or vehicle route-following code.
/// </summary>
public static class TrafficPathfinder
{
    private struct HeapItem
    {
        public long node;
        public float priority;

        public HeapItem(long nodeId, float p)
        {
            node = nodeId;
            priority = p;
        }
    }

    private sealed class MinHeap
    {
        private readonly List<HeapItem> items = new List<HeapItem>();
        public int Count => items.Count;

        public void Push(long node, float priority)
        {
            items.Add(new HeapItem(node, priority));
            int index = items.Count - 1;

            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (items[parent].priority <= items[index].priority)
                    break;

                HeapItem tmp = items[parent];
                items[parent] = items[index];
                items[index] = tmp;
                index = parent;
            }
        }

        public HeapItem Pop()
        {
            HeapItem result = items[0];
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);

            int index = 0;
            while (index < items.Count)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;

                if (left < items.Count &&
                    items[left].priority < items[smallest].priority)
                {
                    smallest = left;
                }

                if (right < items.Count &&
                    items[right].priority < items[smallest].priority)
                {
                    smallest = right;
                }

                if (smallest == index)
                    break;

                HeapItem tmp = items[index];
                items[index] = items[smallest];
                items[smallest] = tmp;
                index = smallest;
            }

            return result;
        }
    }

    public static bool TryFindRoute(
        RoadNetworkManager network,
        long startNode,
        long goalNode,
        float vehicleTopSpeedKmh,
        List<Lane> result,
        bool includeTrafficInCost = false)
    {
        result.Clear();

        if (network == null || startNode == goalNode)
            return startNode == goalNode;

        if (!network.nodesById.ContainsKey(startNode) ||
            !network.nodesById.ContainsKey(goalNode))
        {
            return false;
        }

        MinHeap open = new MinHeap();
        Dictionary<long, float> gScore = new Dictionary<long, float>();
        Dictionary<long, long> previousNode = new Dictionary<long, long>();
        Dictionary<long, Lane> previousLane = new Dictionary<long, Lane>();
        HashSet<long> closed = new HashSet<long>();

        gScore[startNode] = 0f;
        open.Push(
            startNode,
            HeuristicSeconds(network, startNode, goalNode, vehicleTopSpeedKmh)
        );

        while (open.Count > 0)
        {
            HeapItem currentItem = open.Pop();
            long current = currentItem.node;

            if (closed.Contains(current))
                continue;

            if (current == goalNode)
            {
                ReconstructRoute(
                    startNode,
                    goalNode,
                    previousNode,
                    previousLane,
                    result
                );
                return true;
            }

            closed.Add(current);

            if (!network.lanesFromNode.TryGetValue(
                    current,
                    out List<Lane> outgoing) ||
                outgoing == null)
            {
                continue;
            }

            float currentCost = gScore[current];

            foreach (Lane lane in outgoing)
            {
                if (lane == null)
                    continue;

                long neighbour = lane.endNode;
                if (closed.Contains(neighbour))
                    continue;

                float edgeCost = network.GetRoutingCostSeconds(
                    lane,
                    vehicleTopSpeedKmh,
                    includeTrafficInCost
                );

                if (float.IsInfinity(edgeCost))
                    continue;

                float tentative = currentCost + edgeCost;

                if (!gScore.TryGetValue(neighbour, out float known) ||
                    tentative < known)
                {
                    gScore[neighbour] = tentative;
                    previousNode[neighbour] = current;
                    previousLane[neighbour] = lane;

                    float fScore = tentative + HeuristicSeconds(
                        network,
                        neighbour,
                        goalNode,
                        vehicleTopSpeedKmh
                    );

                    open.Push(neighbour, fScore);
                }
            }
        }

        return false;
    }

    private static float HeuristicSeconds(
        RoadNetworkManager network,
        long fromNode,
        long goalNode,
        float vehicleTopSpeedKmh)
    {
        if (!network.nodesById.TryGetValue(fromNode, out RoadNodeData from) ||
            !network.nodesById.TryGetValue(goalNode, out RoadNodeData goal))
        {
            return 0f;
        }

        Vector3 a = new Vector3(
            from.position.x,
            from.position.y,
            from.position.z
        );

        Vector3 b = new Vector3(
            goal.position.x,
            goal.position.y,
            goal.position.z
        );

        float distance = Vector3.Distance(a, b);
        float speedMps = Mathf.Max(0.1f, vehicleTopSpeedKmh / 3.6f);
        return distance / speedMps;
    }

    private static void ReconstructRoute(
        long startNode,
        long goalNode,
        Dictionary<long, long> previousNode,
        Dictionary<long, Lane> previousLane,
        List<Lane> result)
    {
        long current = goalNode;

        while (current != startNode)
        {
            if (!previousLane.TryGetValue(current, out Lane lane) ||
                !previousNode.TryGetValue(current, out long previous))
            {
                result.Clear();
                return;
            }

            result.Add(lane);
            current = previous;
        }

        result.Reverse();
    }
}
