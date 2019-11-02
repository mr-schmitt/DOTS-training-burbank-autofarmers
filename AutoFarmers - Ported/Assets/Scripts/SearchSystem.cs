using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using static Movement;
using static Unity.Mathematics.math;

public class SearchSystem : JobComponentSystem
{
	public EntityCommandBufferSystem ecbs;
    public NativeArray<int> randomValues;
    public Unity.Mathematics.Random rand;
    const int RANDOM_SIZE = 256;

	protected override void OnCreate()
	{
        rand = new Unity.Mathematics.Random(42);
		ecbs = World.GetOrCreateSystem<EntityCommandBufferSystem>();
        randomValues = new NativeArray<int>(RANDOM_SIZE, Allocator.Persistent);
        for (int i=0; i<RANDOM_SIZE; i++)
        {
            randomValues[i] = System.Math.Abs(rand.NextInt());
        }
	}

    [BurstCompile]
    [RequireComponentTag(typeof(NeedsTaskTag))]
    struct SearchSystemJob : IJobForEachWithEntity<Translation, actor_RunTimeComp>
    {
        public EntityCommandBuffer.Concurrent ecb;
        public NativeHashMap<int, int> gridHashMap;
        [ReadOnly] public NativeArray<int> randArray;
        [ReadOnly] public int nextIndex;
        [ReadOnly] public int gridSize;
        [ReadOnly] public int radiusForSearch;
        public enum Intentions : int { None = 0, Rock = 1, Till = 2, Plant = 3, Store=4, MoveToRock = 5, PerformRock = 6, MoveToTill = 7, PerformTill = 8, MoveToPlant = 9, PerformPlanting = 10, MovingToStore=11 };
        public void Execute(Entity entity, int index, [ReadOnly]ref Translation translation, ref actor_RunTimeComp movementComponent)
        {
            Debug.Log("finding new task");
            // set new task: should be more complicated
            int taskValue = (randArray[(nextIndex + index) % randArray.Length] % 3) + 1; // can be rock or till
            float2 pos = new float2(translation.Value.x, translation.Value.z);
            float2 foundLocation;
            
            if (taskValue == (int)Intentions.Rock ) {
                foundLocation = GridData.Search(gridHashMap, pos, radiusForSearch, taskValue, gridSize, gridSize);
            } else if (taskValue == (int)Intentions.Plant)
            {
                // need to search for 2 which is the tilled soil
                foundLocation = GridData.Search(gridHashMap, pos, radiusForSearch, 2, gridSize, gridSize);

            }
            else if (taskValue == (int)Intentions.Store)
            {
                // searches for the plants to go harvest them - 3
                foundLocation = GridData.Search(gridHashMap, pos, radiusForSearch, 3, gridSize, gridSize);

            }
            else // till only looks at things that don't exist in the grid - 0
            {
                Unity.Mathematics.Random rand = new Unity.Mathematics.Random((uint)nextIndex);
                // we look for a default spot to put a tilled thing
                float2 nextPos = new float2(rand.NextInt() % (gridSize-1), rand.NextInt() % (gridSize-1));
                bool found = false;
                foundLocation = GridData.Search(gridHashMap, nextPos, radiusForSearch, 0, gridSize, gridSize);
                int value;
                if (gridHashMap.TryGetValue(GridData.ConvertToHash((int)nextPos.x, (int)nextPos.y), out value))
                {
                    //Debug.Log("random location didn't work");
                    foundLocation = GridData.Search(gridHashMap, pos, radiusForSearch, 3, gridSize, gridSize);
                } 

                
            }
            float2 findMiddle = MovementJob.FindMiddlePos(pos, foundLocation);
            Debug.Log("Start: " + pos.x + " " + pos.y + " middle : " + findMiddle.x + " " + findMiddle.y + " target pos : " +
                movementComponent.targetPos.x + " " + movementComponent.targetPos.y);
            var rockPos = GridData.FindTheRock(gridHashMap, pos, MovementJob.FindMiddlePos(pos, foundLocation), foundLocation, gridSize, gridSize);
            if (foundLocation.x != -1 && foundLocation.y != -1 && rockPos.x != -1)
            {
                rockPos = new float2(rockPos.x + 0.5f, rockPos.y + 0.5f);
                movementComponent.targetPos = rockPos;
                Debug.Log("Updated Position to " + rockPos + "Actor is now chasing a rock");
                var data = new actor_RunTimeComp { startPos = pos, speed = 2, targetPos = rockPos, intent = 5 };
                movementComponent = data;
                ecb.RemoveComponent(index, entity, typeof(NeedsTaskTag));
                ecb.AddComponent(index, entity, typeof(MovingTag));
                int key = GridData.ConvertToHash((int)rockPos.x, (int)rockPos.y);
                gridHashMap.Remove(key);
            }
            else
            {
                if (foundLocation.x != -1 && foundLocation.y != -1)
                {
                    foundLocation = new float2(foundLocation.x + 0.5f, foundLocation.y + 0.5f);
                    actor_RunTimeComp data = new actor_RunTimeComp { startPos = pos, speed = 2, targetPos = foundLocation, intent = taskValue };

                    ecb.SetComponent(index, entity, data);

                    Debug.Log("doing a task and about to move: " + pos.x + " " + pos.y + 
                        " target is : " + movementComponent.targetPos.x + " " + movementComponent.targetPos.y);
                    ecb.RemoveComponent(index, entity, typeof(NeedsTaskTag));
                    ecb.AddComponent(index, entity, typeof(MovingTag));

                    if (taskValue == (int)Intentions.Rock)
                    {
                        int key = GridData.ConvertToHash((int)foundLocation.x, (int)foundLocation.y);
                        gridHashMap.Remove(key);
                    }
                    if (taskValue == (int)Intentions.Plant)
                    {
                        
                        int key = GridData.ConvertToHash((int)foundLocation.x, (int)foundLocation.y);
                        gridHashMap.Remove(key);
                    }
                    if (taskValue == (int)Intentions.Store)
                    {
                        int key = GridData.ConvertToHash((int)foundLocation.x, (int)foundLocation.y);
                        gridHashMap.Remove(key);
                    }

                }
                else
                {
                    Debug.Log("location wasn't found - find another task");
                }
            }
        }
    }
    
    protected override JobHandle OnUpdate(JobHandle inputDependencies)
    {
        int index = System.Math.Abs(rand.NextInt()) % randomValues.Length;
        var job = new SearchSystemJob();
        job.gridHashMap = GridData.gridStatus;
        job.randArray = randomValues;
        job.nextIndex = index;
        job.ecb = ecbs.CreateCommandBuffer().ToConcurrent();
        job.gridSize = GridData.width;
        job.radiusForSearch = 15;

        //Debug.Log("nextInt: " + (randomValues[(index) % randomValues.Length]%4 + 1));
        var jobHandle = job.ScheduleSingle(this, inputDependencies);
        ecbs.AddJobHandleForProducer(jobHandle);
        return jobHandle;
    }

    protected override void OnDestroy()
    {
        if (randomValues.IsCreated)
        {
            randomValues.Dispose();
        }
        base.OnDestroy();
    }
}