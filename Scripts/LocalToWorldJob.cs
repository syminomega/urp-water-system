using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
#if UNITY_6000_5_OR_NEWER
using BuoyancyObjectId = UnityEngine.EntityId;
#else
using BuoyancyObjectId = System.Int32;
#endif

public static class LocalToWorldJob
{
    private static readonly Dictionary<BuoyancyObjectId, TransformLocalToWorld> Data = new Dictionary<BuoyancyObjectId, TransformLocalToWorld>();

    [BurstCompile]
    struct LocalToWorldConvertJob : IJob
    {
        [WriteOnly] public NativeArray<float3> PositionsWorld;
        [ReadOnly] public Matrix4x4 Matrix;
        [ReadOnly] public NativeArray<float3> PositionsLocal;

        // The code actually running on the job
        public void Execute()
        {
            for (var i = 0; i < PositionsLocal.Length; i++)
            {
                var pos = float4.zero;
                pos.xyz = PositionsLocal[i];
                pos.w = 1f;
                pos = Matrix * pos;
                PositionsWorld[i] = pos.xyz;
            }
        }
    }

    public static void SetupJob(BuoyancyObjectId guid, Vector3[] positions, ref NativeArray<float3> output)
    {
        var jobData = new TransformLocalToWorld
        {
            PositionsWorld = output,
            PositionsLocal = new NativeArray<float3>(positions.Length, Allocator.Persistent)
        };
        
        for (var index = 0; index < positions.Length; index++)
        {
            jobData.PositionsLocal[index] = positions[index];
        }
        
        Data.Add(guid, jobData);
    }

    public static void ScheduleJob(BuoyancyObjectId guid, Matrix4x4 localToWorld)
    {
        if (Data[guid].Processing)
        {
            return;
        }
        
        Data[guid].Job = new LocalToWorldConvertJob()
        {
            PositionsWorld = Data[guid].PositionsWorld,
            PositionsLocal = Data[guid].PositionsLocal,
            Matrix = localToWorld
        };
        
        Data[guid].Handle = Data[guid].Job.Schedule();
        Data[guid].Processing = true;
        JobHandle.ScheduleBatchedJobs();
    }

    public static void CompleteJob(BuoyancyObjectId guid)
    {
        Data[guid].Handle.Complete();
        Data[guid].Processing = false;
    }

    public static void Cleanup(BuoyancyObjectId guid)
    {
        if (!Data.ContainsKey(guid))
        {
            return;
        }
        Data[guid].Handle.Complete();
        Data[guid].PositionsWorld.Dispose();
        Data[guid].PositionsLocal.Dispose();
        Data.Remove(guid);
    }

    class TransformLocalToWorld
    {
        public NativeArray<float3> PositionsLocal;
        public NativeArray<float3> PositionsWorld;
        public JobHandle Handle;
        public LocalToWorldConvertJob Job;
        public bool Processing;
    }
}
