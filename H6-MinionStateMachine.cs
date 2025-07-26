
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

using GameAI;


namespace GameAIStudent
{

    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(MinionScript))]
    public class MinionStateMachine : MonoBehaviour
    {
        public const string StudentAuthorName = "Dongning Li";

        public const string GlobalTransitionStateName = "GlobalTransition";
        public const string CollectBallStateName = "CollectBall";
        public const string GoToThrowSpotStateName = "GoToThrowBall";
        public const string ThrowBallStateName = "ThrowBall";
        public const string DefensiveDemoStateName = "DefensiveDemo";
        public const string GoToPrisonStateName = "GoToPrison";
        public const string LeavePrisonStateName = "LeavePrison";
        public const string GoHomeStateName = "GoHome";
        public const string RescueStateName = "Rescue";
        public const string RestStateName = "Rest";


        // For throws...
        public static float MaxAllowedThrowPositionError = (0.25f + 0.5f) * 0.99f;

        // Data that each FSM state gets initialized with (passed as init param)
        FiniteStateMachine<MinionFSMData> fsm;

        public MinionScript Minion { get; private set; }

        PrisonDodgeballManager Mgr;
        public TeamShare TeamData { get; private set; }

        struct MinionFSMData
        {
            public MinionStateMachine MinionFSM { get; private set; }
            public MinionScript Minion { get; private set; }
            public PrisonDodgeballManager Mgr { get; private set; }
            public PrisonDodgeballManager.Team Team { get; private set; }
            public TeamShare TeamData { get; private set; }

            public MinionFSMData(
                MinionStateMachine minionFSM,
                MinionScript minion,
                PrisonDodgeballManager mgr,
                PrisonDodgeballManager.Team team,
                TeamShare teamData
                )
            {
                MinionFSM = minionFSM;
                Minion = minion;
                Mgr = mgr;
                Team = team;
                TeamData = teamData;
            }
        }

        // Simple demo of shared info amongst the team
        // You can modify this as necessary for advanced team strategy
        // Tracking teammates is added to get you started.
        // Also, some expensive queries of opponent and dodgeballs are
        // shared across the team
        public class TeamShare
        {
            public PrisonDodgeballManager.Team Team { get; private set; }
            public MinionScript[] TeamMates { get; private set; }
            public int TeamSize { get; private set; }
            public int NumBalls { get; private set; }
            protected int currTeamMateRegSpot = 0;

            // These are used to track whether data is stale
            protected float timeOfDBQuery = float.MinValue;

            protected PrisonDodgeballManager.DodgeballInfo[] dbInfo;

            public PrisonDodgeballManager.DodgeballInfo[] DBInfo
            {
                get
                {
                    var t = Time.timeSinceLevelLoad;

                    if (t != timeOfDBQuery)
                    {
                        timeOfDBQuery = t;
                        PrisonDodgeballManager.Instance.GetAllDodgeballInfo(Team, ref dbInfo, true);
                    }

                    return dbInfo;
                }
                private set { dbInfo = value; }
            }

            public TeamShare(PrisonDodgeballManager.Team team, int teamSize, int numBalls)
            {
                Team = team;
                TeamSize = teamSize;
                NumBalls = numBalls;
                TeamMates = new MinionScript[TeamSize];

                DBInfo = new PrisonDodgeballManager.DodgeballInfo[NumBalls];
            }

            public void AddTeamMember(MinionScript m)
            {
                TeamMates[currTeamMateRegSpot] = m;
                ++currTeamMateRegSpot;
            }

            public bool IsFullyInitialized
            {
                get => currTeamMateRegSpot >= TeamSize;
            }

        }

        // Create a base class for our states to have access to the parent MinionStateMachine, and other info
        // This class can be modified!
        abstract class MinionStateBase
        {
            public virtual string Name => throw new System.NotImplementedException();

            protected IFiniteStateMachine<MinionFSMData> ParentFSM;
            protected MinionStateMachine MinionFSM;
            protected MinionScript Minion;
            protected PrisonDodgeballManager Mgr;
            protected PrisonDodgeballManager.Team Team;
            protected TeamShare TeamData;


            public virtual void Init(IFiniteStateMachine<MinionFSMData> parentFSM,
                MinionFSMData minFSMData)
            {
                ParentFSM = parentFSM;
                MinionFSM = minFSMData.MinionFSM;
                Minion = minFSMData.Minion;
                Mgr = minFSMData.Mgr;
                Team = minFSMData.Team;
                TeamData = minFSMData.TeamData;
            }

            // Note: You can add extra methods here that you want to be available to all states
            protected bool FindClosestAvailableDodgeball(
                out PrisonDodgeballManager.DodgeballInfo dodgeballInfo)
            {
                var dist = float.MaxValue;
                bool found = false;

                dodgeballInfo = default;

                if (TeamData == null)
                    return false;

                var dbInfo = TeamData.DBInfo;

                if (dbInfo == null)
                    return false;

                foreach (var db in dbInfo)
                {
                    if (!db.IsHeld && db.State == PrisonDodgeballManager.DodgeballState.Neutral && db.Reachable)
                    {
                        var d = Vector3.Distance(db.Pos, Minion.transform.position);
                        if (d < dist)
                        {
                            found = true;
                            dist = d;
                            dodgeballInfo = db;
                        }
                    }
                }
                return found;
            }

            public bool FindRescuableTeammate(out MinionScript firstHelplessMinion)
            {
                firstHelplessMinion = null;

                if (TeamData == null)
                    return false;

                var teammates = TeamData.TeamMates;

                if (teammates == null)
                    return false;

                foreach (var m in teammates)
                {
                    if (m == null)
                        continue;

                    if (m.CanBeRescued)
                    {
                        firstHelplessMinion = m;
                        return true;
                    }
                }
                return false;
            }

            protected void InternalEnter()
            {
                MinionFSM.Minion.DisplayText(Name);
            }

            // globalTransition parameter is to notify if transition was triggered
            // by a global transition (wildcard)
            public virtual void Exit(bool globalTransition) { }
            public virtual void Exit() { Exit(false); }

            public virtual StateTransitionBase<MinionFSMData> Update()
            {
                return null;
            }
        }

        // Create a base class for our states to have access to the parent MinionStateMachine, and other info
        abstract class MinionState : MinionStateBase, IState<MinionFSMData>
        {
            public virtual void Enter() { InternalEnter(); }
        }

        // Create a base class for our states to have access to the parent MinionStateMachine, and other info
        abstract class MinionState<S0> : MinionStateBase, IState<MinionFSMData, S0>
        {
            public virtual void Enter(S0 s) { InternalEnter(); }
        }

        // Create a base class for our states to have access to the parent MinionStateMachine, and other info
        abstract class MinionState<S0, S1> : MinionStateBase, IState<MinionFSMData, S0, S1>
        {
            public virtual void Enter(S0 s0, S1 s1) { InternalEnter(); }
        }

        // If you need MinionState<>s with more parameters (up to four total), you can add them following the pattern above

        // Go get a ball!
        class CollectBallState : MinionState
        {
            public override string Name => CollectBallStateName;

            bool hasDestBall = false;
            PrisonDodgeballManager.DodgeballInfo destBall;
            float lastTeammateCheckTime;
            const float TeammateCheckInterval = 0.5f;
            

            public override void Init(IFiniteStateMachine<MinionFSMData> parentFSM, MinionFSMData minFSMData)
            {
                base.Init(parentFSM, minFSMData);
            }

            public override void Enter()
            {
                base.Enter();
                
                if (FindClosestAvailableDodgeball(out destBall))
                {
                    hasDestBall = true;
                    Minion.GoTo(destBall.Pos);
                }
            }

            public override void Exit(bool globalTransition)
            {

            }
            protected bool IsBallBeingCollectedByTeammates(int ballIndex)
            {


                float TeammateCheckInterval = Mgr.TeamSize >= 4 ? 0.5f : 0.1f;
                // Only check periodically for performance
                if (Time.timeSinceLevelLoad - lastTeammateCheckTime < TeammateCheckInterval)
                    return false;

                lastTeammateCheckTime = Time.timeSinceLevelLoad;

                if (TeamData?.TeamMates == null) 
                    return false;

                foreach (var mate in TeamData.TeamMates)
                {
                    if (mate != null && mate != Minion && mate.DodgeballIndex == ballIndex)
                    {
                        return true;
                    }
                }
                return false;
            }
            public override StateTransitionBase<MinionFSMData> Update()
            {
                bool isEarlyGame = Time.timeSinceLevelLoad < 5f;
                bool preventTeammateBallConflict = (Mgr.TeamSize >= 4) && !isEarlyGame;

                // could pick up a ball accidentally before getting to desired ball
                if (Minion.HasBall)
                    return ParentFSM.CreateStateTransition(GoToThrowSpotStateName);

                var dbInfo = TeamData.DBInfo;
                if (dbInfo == null) return null;

                // Update status of target ball
                if (hasDestBall)
                {
                    destBall = dbInfo[destBall.Index];

                    bool teammateConflict = preventTeammateBallConflict && IsBallBeingCollectedByTeammates(destBall.Index);

                    if (destBall.IsHeld || !destBall.Reachable || teammateConflict
                        || (destBall.State != PrisonDodgeballManager.DodgeballState.Neutral &&
                            destBall.State != PrisonDodgeballManager.DodgeballState.Team))
                    {
                        hasDestBall = false;
                    }
                }
                if (hasDestBall)
                {
                    Minion.GoTo(destBall.NavMeshPos);
                    if (Mgr.TeamSize >= 3 && FindClosestAvailableDodgeball(out var closerBall))
                    {
                        bool shouldSwitch = false;

                        // 3v3-1b logic: Always go for closest ball
                        if (Mgr.BallsPerTeam == 1)
                        {
                            shouldSwitch = 
                                          Vector3.Distance(closerBall.Pos, Minion.transform.position) <
                                          Vector3.Distance(destBall.Pos, Minion.transform.position) * 0.8f;
                        }
                        // 3v3-2b/3b logic: Role-based behavior
                        else
                        {
                            bool shouldBeAggressive = (Mgr.TeamSize >= 4) && (Minion.SpawnIndex < 2);
                            bool isBetterBall = closerBall.State == PrisonDodgeballManager.DodgeballState.Neutral ||
                                              closerBall.State == PrisonDodgeballManager.DodgeballState.Team;

                            shouldSwitch = !IsBallBeingCollectedByTeammates(closerBall.Index) &&
                                         (shouldBeAggressive || closerBall.State != PrisonDodgeballManager.DodgeballState.Opponent) &&
                                         isBetterBall &&
                               Vector3.Distance(closerBall.Pos, Minion.transform.position) <
                               Vector3.Distance(destBall.Pos, Minion.transform.position) * 0.8f;
                        }

                        if (shouldSwitch)
                        {
                            destBall = closerBall;
                            Minion.GoTo(destBall.NavMeshPos);
                        }
                    }
                    return null;
                }
                if (FindClosestAvailableDodgeball(out destBall))
                {
                    hasDestBall = true;
                    Minion.GoTo(destBall.NavMeshPos);
                    return null;
                }
                return ParentFSM.CreateStateTransition(DefensiveDemoStateName);
            }
        }

        // This state gets the minion close to the enemy for a throw (or a rescue of a buddy)
        class GoToThrowSpotState : MinionState
        {
            public override string Name => GoToThrowSpotStateName;

            public override void Init(IFiniteStateMachine<MinionFSMData> parentFSM, MinionFSMData minFSMData)
            {
                base.Init(parentFSM, minFSMData);
            }

            public override void Enter()
            {
                base.Enter();
                float advanceRatio = 0.5f;

                // Adjust for 4v4 by spreading out more
                if (Mgr.TeamSize >= 4)
                {
                    advanceRatio = Minion.HasBall ? 0.6f : 0.3f; // More conservative
                    float spreadFactor = 2.5f * (Minion.SpawnIndex / (float)Mgr.TeamSize);

                    Vector3 baseSpot = Vector3.Lerp(
                        Mgr.TeamHome(Team).position,
                        Mgr.TeamAdvance(Team).position,
                        advanceRatio
                    );

                    // Spread out along the width of the court
                    Vector3 rightOffset = Mgr.TeamGutterEntranceRight(Team).position - Mgr.TeamHome(Team).position;
                    Vector3 spreadOffset = rightOffset.normalized * (spreadFactor - 1.25f);

                    Minion.GoTo(baseSpot + spreadOffset);
                }
                else
                {
                    // Keep your original winning 1v1/2v2/3v3 logic
                    Vector3 throwSpot = Vector3.Lerp(
                        Mgr.TeamHome(Team).position,
                        Mgr.TeamAdvance(Team).position,
                        advanceRatio
                    );
                    Minion.GoTo(throwSpot);
                }
            }

            public override void Exit(bool globalTransition)
            {

            }

            public override StateTransitionBase<MinionFSMData> Update()
            {

                // just in case something bad happened
                if (!Minion.HasBall)
                {
                    return ParentFSM.CreateStateTransition(CollectBallStateName);
                }

                if (Minion.ReachedTarget())
                {
                    if (FindRescuableTeammate(out var m))
                    {
                        return ParentFSM.CreateStateTransition<MinionScript>(RescueStateName, m, true);
                    }
                    else
                        return ParentFSM.CreateStateTransition(ThrowBallStateName);
                }

                return null;
            }
        }

        // Rescue a buddy
        class RescueState : MinionState<MinionScript>
        {
            public override string Name => RescueStateName;

            MinionScript buddy;

            public override void Init(IFiniteStateMachine<MinionFSMData> parentFSM, MinionFSMData minFSMData)
            {
                base.Init(parentFSM, minFSMData);
            }

            public override void Enter(MinionScript m)
            {
                base.Enter(m);

                buddy = m;

                Minion.FaceTowards(buddy.transform.position);

            }

            public override void Exit(bool globalTransition)
            {

            }

            public override StateTransitionBase<MinionFSMData> Update()
            {

                // just in case something bad happened
                if (!Minion.HasBall)
                {
                    return ParentFSM.CreateStateTransition(CollectBallStateName);
                }

                if (buddy == null || !buddy.CanBeRescued)
                {

                    if (!FindRescuableTeammate(out buddy))
                    {
                        buddy = null;
                    }
                }

                // Nothing to do without buddy in prison...
                if (buddy == null)
                    // we should have a ball still...
                    return ParentFSM.CreateStateTransition(ThrowBallStateName);


                var canThrow = ThrowMethods.PredictThrow(Minion.HeldBallPosition, Minion.ThrowSpeed, Physics.gravity, buddy.transform.position,
                        buddy.Velocity, buddy.transform.forward, MaxAllowedThrowPositionError,
                        out var univVDir, out var speedScalar, out var interceptT, out var altT);


                var intercept = Minion.HeldBallPosition + univVDir * speedScalar * interceptT;
                Minion.FaceTowardsForThrow(intercept);

                if (canThrow)
                {
                    var speedNorm = speedScalar / Minion.ThrowSpeed;

                    if (Minion.ThrowBall(univVDir, speedNorm))
                        return ParentFSM.CreateStateTransition(CollectBallStateName);
                }
                return null;
            }
        }

        // Throw the ball at the enemy
        class ThrowBallState : MinionState
        {
            public override string Name => ThrowBallStateName;

            int opponentIndex = -1;
            PrisonDodgeballManager.OpponentInfo opponentInfo;
            bool hasOpponent = false;

            public override void Init(IFiniteStateMachine<MinionFSMData> parentFSM, MinionFSMData minFSMData)
            {
                base.Init(parentFSM, minFSMData);
            }


            public override void Enter()
            {
                base.Enter();

                if (Mgr.FindClosestNonPrisonerOpponentIndex(Minion.transform.position, Team, out opponentIndex))
                {
                    if (hasOpponent = Mgr.GetOpponentInfo(Team, opponentIndex, out opponentInfo))
                    {
                        Minion.FaceTowards(opponentInfo.Pos);
                    }
                }
            }

            public override void Exit(bool globalTransition)
            {

            }

            public override StateTransitionBase<MinionFSMData> Update()
            {
                // just in case something bad happened
                if (!Minion.HasBall)
                {
                    return ParentFSM.CreateStateTransition(CollectBallStateName);
                }

                // Check if opponent still valid
                if (!(hasOpponent = Mgr.GetOpponentInfo(Team, opponentIndex, out opponentInfo)) ||
                    opponentInfo.IsPrisoner || opponentInfo.IsFreedPrisoner )
                {
                    if (Mgr.FindClosestNonPrisonerOpponentIndex(Minion.transform.position, Team, out opponentIndex)
                    || !Mgr.GetOpponentInfo(Team, opponentIndex, out opponentInfo))
                    {
                        return ParentFSM.CreateStateTransition(DefensiveDemoStateName);
                    }
                    hasOpponent = true;
                }

                int navmask = NavMesh.AllAreas;
                var selection = ShotSelection.SelectThrow(Minion, opponentInfo, navmask, 
                MaxAllowedThrowPositionError, Time.deltaTime, out var projectileDir, 
                out var projectileSpeed, out var interceptT, out var interceptPos);

                if (selection == ShotSelection.SelectThrowReturn.DoThrow)
                {
                    Minion.FaceTowardsForThrow(interceptPos);
                    var speedFactor = Mathf.Min(1f, projectileSpeed / Minion.ThrowSpeed);
                    var throwRes = Minion.ThrowBall(projectileDir, speedFactor);

                    if (throwRes)
                    {
                        // Minion.FaceTowardsForThrow(interceptPos);
                        return ParentFSM.CreateStateTransition(CollectBallStateName);
                    }
                }
                
                Minion.FaceTowardsForThrow(selection == ShotSelection.SelectThrowReturn.NoThrowTargettingFailed ? 
                opponentInfo.Pos : interceptPos);
                return null;     
                }
            }
        
        // A not very effective defensive strategy. Mainly a demonstration of calling
        // Minion.Evade()
        class DefensiveDemoState : MinionState
        {
            public override string Name => DefensiveDemoStateName;

            float lastEvade;
            float evadeWaitTimeSec;
            bool doPause = false;
            float pauseStart;
            float pauseDuration;

            public override void Init(IFiniteStateMachine<MinionFSMData> parentFSM, MinionFSMData minFSMData)
            {
                base.Init(parentFSM, minFSMData);
            }

            protected bool RandomGoTo()
            {
                var r = Minion.GoTo(Mgr.TeamHome(Team).position + 6f * (new Vector3(Random.value, 0f, Random.value)));

                if (!r)
                {
                    Debug.LogWarning("Could not GOTO in DefenseDemoState");
                }
                return r;
            }

            public override void Enter()
            {
                base.Enter();

                RandomGoTo();

                lastEvade = Time.timeSinceLevelLoad;

                evadeWaitTimeSec = 2f * Minion.EvadeCoolDownTimeSec + 0.1f;
            }

            public override void Exit(bool globalTransition)
            {

            }

            public override StateTransitionBase<MinionFSMData> Update()
            {

                if (Minion.HasBall)
                    return ParentFSM.CreateStateTransition(GoToThrowSpotStateName);

                PrisonDodgeballManager.DodgeballInfo ball;

                if (FindClosestAvailableDodgeball(out ball))
                {
                    return ParentFSM.CreateStateTransition(CollectBallStateName);
                }

                if (!doPause && Minion.ReachedTarget())
                {
                    pauseStart = Time.timeSinceLevelLoad;
                    doPause = true;
                    pauseDuration = Random.value * 3f;
                }

                if (doPause)
                {
                    Minion.FaceTowards(Mgr.TeamPrison(Team).position);

                    if (Time.timeSinceLevelLoad - pauseStart >= pauseDuration)
                    {
                        doPause = false;
                        RandomGoTo();
                    }
                }
                else if (Time.timeSinceLevelLoad - lastEvade >= evadeWaitTimeSec)
                {

                    lastEvade = Time.timeSinceLevelLoad;

                    var r = Random.Range(0, 3);

                    MinionScript.EvasionDirection ev;

                    switch (r)
                    {
                        case 0:
                            ev = MinionScript.EvasionDirection.Brake;
                            break;
                        case 1:
                            ev = MinionScript.EvasionDirection.Left;
                            break;
                        case 2:
                            ev = MinionScript.EvasionDirection.Right;
                            break;
                        default:
                            ev = MinionScript.EvasionDirection.Brake;
                            break;
                    }

                    Minion.Evade(ev, Random.Range(0.6f, 1.0f));
                }

                return null;
            }
        }


        // Go directly to jail. Do not pass go. Do not collect $200 
        class GoToPrisonState : MinionState
        {
            public override string Name => GoToPrisonStateName;

            int waypointIndex = 0;


            public override void Init(IFiniteStateMachine<MinionFSMData> parentFSM, MinionFSMData minFSMData)
            {
                base.Init(parentFSM, minFSMData);
            }


            public override void Enter()
            {
                base.Enter();

                waypointIndex = 0;

                Minion.GoTo(Mgr.TeamGutterEntranceLeft(Team).position);
            }

            public override void Exit(bool globalTransition)
            {

            }

            public override StateTransitionBase<MinionFSMData> Update()
            {

                if (!Minion.IsPrisoner)
                {
                    return ParentFSM.CreateStateTransition(LeavePrisonStateName);
                }

                if (Minion.ReachedTarget())
                {
                    if (waypointIndex == 0)
                    {
                        ++waypointIndex;
                        Minion.GoTo(Mgr.TeamGutterEndLeft(Team).position);
                    }
                    else if (waypointIndex == 1)
                    {
                        ++waypointIndex;
                        Minion.GoTo(Mgr.TeamPrison(Team).position);
                    }
                    else
                    {
                        Minion.FaceTowards(Mgr.TeamHome(Team).position);
                    }
                }

                return null;
            }
        }

        // Free! 
        class LeavePrisonState : MinionState
        {
            public override string Name => LeavePrisonStateName;

            int waypointIndex = 0;

            public override void Init(IFiniteStateMachine<MinionFSMData> parentFSM, MinionFSMData minFSMData)
            {
                base.Init(parentFSM, minFSMData);
            }


            public override void Enter()
            {
                base.Enter();

                waypointIndex = 0;

                Minion.GoTo(Mgr.TeamGutterEndRight(Team).position);
            }

            public override void Exit(bool globalTransition)
            {

            }

            public override StateTransitionBase<MinionFSMData> Update()
            {

                if (Minion.ReachedTarget())
                {
                    if (waypointIndex == 0)
                    {
                        ++waypointIndex;
                        Minion.GoTo(Mgr.TeamGutterEntranceRight(Team).position);
                    }
                    else
                    {
                        if (Minion.HasBall)
                            return ParentFSM.CreateStateTransition(GoToThrowSpotStateName);
                        else
                            return ParentFSM.CreateStateTransition(GoHomeStateName);
                    }
                }

                return null;
            }
        }


        // Going home. Maybe after a jailbreak
        class GoHomeState : MinionState
        {
            public override string Name => GoHomeStateName;

            public override void Init(IFiniteStateMachine<MinionFSMData> parentFSM, MinionFSMData minFSMData)
            {
                base.Init(parentFSM, minFSMData);
            }


            public override void Enter()
            {
                base.Enter();

                if (!Minion.GoTo(Mgr.TeamHome(Team).position))
                {
                    Debug.LogWarning($"Could not find a way home! NavMesh Mask: {Minion.NavMeshMaskToString()}");
                }
            }

            public override void Exit(bool globalTransition)
            {

            }

            public override StateTransitionBase<MinionFSMData> Update()
            {

                if (Minion.ReachedTarget())
                {
                    return ParentFSM.CreateStateTransition(CollectBallStateName);
                }

                return null;
            }
        }


        class RestState : MinionState
        {
            public override string Name => RestStateName;

            public override void Enter()
            {
                base.Enter();

                if (!Minion.GoTo(Mgr.TeamHome(Team).position))
                {
                    Debug.LogWarning($"Could not find a way home! NavMesh Mask: {Minion.NavMeshMaskToString()}");
                }
            }

            public override StateTransitionBase<MinionFSMData> Update()
            {
                return null;
            }
        }


        // This is a special state that never exits. It coexists with the current state.
        // It's always evaluated first. It's only job is supposed to identify global/wildcard
        // transitions (it shouldn't do anything that modifies anything externally other than
        // return a desired transition).
        class GlobalTransitionState : MinionState
        {
            public override string Name => GlobalTransitionStateName;

            bool wasPrisioner = false;

            public override void Init(IFiniteStateMachine<MinionFSMData> parentFSM, MinionFSMData minFSMData)
            {
                base.Init(parentFSM, minFSMData);
            }


            public override void Enter()
            {
                base.Enter();
            }

            // The global state never exits
            //public override void Exit(bool globalTransition)
            //{
            //}

            public override StateTransitionBase<MinionFSMData> Update()
            {

                if (Mgr.IsGameOver && !ParentFSM.CurrentState.Name.Equals(RestStateName))
                {
                    return ParentFSM.CreateStateTransition(RestStateName);
                }
                else if (Minion.IsPrisoner && !wasPrisioner)
                {
                    // Just switched to prisoner! Uh oh. Gotta head to prison. :-(

                    wasPrisioner = true;

                    return ParentFSM.CreateStateTransition(GoToPrisonStateName);
                }
                else if (!Minion.IsPrisoner && wasPrisioner)
                {
                    wasPrisioner = false;
                }
                return null;
            }
        }


        private void Awake()
        {
            Minion = GetComponent<MinionScript>();

            if (Minion == null)
            {
                Debug.LogWarning("No minion script");
            }
        }


        protected void InitTeamData()
        {
            Mgr.SetTeamText(Minion.Team, StudentName);

            var o = Mgr.GetTeamDataShare(Minion.Team);

            if (o == null)
            {
                TeamData = new TeamShare(Minion.Team, Mgr.TeamSize, Mgr.TotalBalls);
                Mgr.SetTeamDataShare(Minion.Team, TeamData);
            }
            else
            {
                TeamData = o as TeamShare;

                if (TeamData == null)
                {
                    Debug.LogWarning("TeamData is null!");
                }

            }

            TeamData.AddTeamMember(Minion);
        }


        // Start is called before the first frame update
        protected void Start()
        {

            Mgr = PrisonDodgeballManager.Instance;

            InitTeamData();

            var minionFSMData = new MinionFSMData(this, Minion, Mgr, Minion.Team, TeamData);

            fsm = new FiniteStateMachine<MinionFSMData>(minionFSMData);

            // Handles global/wildcard transitions. This state is a co-state that
            // never exits. Triggered transitions only change the current state.
            // The global state should only handle initiating transitions
            fsm.SetGlobalTransitionState(new GlobalTransitionState());

            fsm.AddState(new CollectBallState(), true);
            fsm.AddState(new GoToThrowSpotState());
            fsm.AddState(new ThrowBallState());
            fsm.AddState(new DefensiveDemoState());
            fsm.AddState(new GoToPrisonState());
            fsm.AddState(new LeavePrisonState());
            fsm.AddState(new GoHomeState());
            fsm.AddState(new RescueState());
            fsm.AddState(new RestState());

            //MinionStateMachine, GameAIStudentWork, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
            //Debug.Log(this.GetType().AssemblyQualifiedName);

        }

        protected void Update()
        {
            // Don't start until all the team is ready to go
            if (TeamData == null || !TeamData.IsFullyInitialized)
                return;

            fsm.Update();

            // For debugging, could repurpose the DisplayText of the Minion.
            // To do so affecting all states, implement the FSM's Update like so:
            //Minion.DisplayText(Minion.NavMeshCurrentSurfaceToString());

        }

    }
}
