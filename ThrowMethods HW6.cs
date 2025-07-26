
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

using GameAI;


namespace GameAIStudent
{

    public class ThrowMethods
    {

        public const string StudentName = "Dongning Li";


        // Note: You have to implement the following method with prediction:
        // Either directly solved (e.g. Law of Cosines or similar) or iterative.
        // You cannot modify the method signature. However, if you want to do more advanced
        // prediction (such as analysis of the navmesh) then you can make another method that calls
        // this one. 
        // Be sure to run the editor mode unit test to confirm that this method runs without
        // any gamemode-only logic
        public static bool PredictThrow(
            // The initial launch position of the projectile
            Vector3 projectilePos,
            // The initial ballistic speed of the projectile
            float maxProjectileSpeed,
            // The gravity vector affecting the projectile (likely passed as Physics.gravity)
            Vector3 projectileGravity,
            // The initial position of the target
            Vector3 targetInitPos,
            // The constant velocity of the target (zero acceleration assumed)
            Vector3 targetConstVel,
            // The forward facing direction of the target. Possibly of use if the target
            // velocity is zero
            Vector3 targetForwardDir,
            // For algorithms that approximate the solution, this sets a limit for how far
            // the target and projectile can be from each other at the interceptT time
            // and still count as a successful prediction
            float maxAllowedErrorDist,
            // Output param: The solved projectileDir for ballistic trajectory that intercepts target
            out Vector3 projectileDir,
            // Output param: The speed the projectile is launched at in projectileDir such that
            // there is a collision with target. projectileSpeed must be <= maxProjectileSpeed
            out float projectileSpeed,
            // Output param: The time at which the projectile and target collide
            out float interceptT,
            // Output param: An alternate time at which the projectile and target collide
            // Note that this is optional to use and does NOT coincide with the solved projectileDir
            // and projectileSpeed. It is possibly useful to pass on to an incremental solver.
            // It only exists to simplify compatibility with the ShootingRange
            out float altT)
        {
            // TODO implement an accurate throw with prediction. This is just a placeholder
            projectileDir = Vector3.zero;
            projectileSpeed = 0f;
            interceptT = -1f;
            altT = -1f;

            Vector3 gravity = projectileGravity;
            const float holdbackFactor = 1.0f;            
            
            float maxInterceptTime = 5f;
            float maxTargetDistance = 40f;
            // float minAllowedError = 0.5f;

            float distanceToTarget = Vector3.Distance(projectilePos, targetInitPos);
            if (distanceToTarget > maxTargetDistance)
                return false;

            float targetSpeed = targetConstVel.magnitude;
            float escapeDot = Vector3.Dot((targetInitPos - projectilePos).normalized, targetConstVel.normalized);
            if (targetSpeed > 7f && escapeDot > 0.9f && distanceToTarget > 30f)
                return false;
                
            // zero-gravity case
            if (gravity.magnitude < Mathf.Epsilon)
            {
                // Simple linear motion prediction
                Vector3 relativePos = targetInitPos - projectilePos;
                Vector3 relativeVel = targetConstVel;

                float a = relativeVel.sqrMagnitude;
                float b = 4f * Vector3.Dot(relativeVel, gravity) - 4f * Vector3.Dot(relativePos, gravity);

                float c = 4f * relativePos.sqrMagnitude - 4f * Vector3.Dot(relativePos, relativeVel) 
             + 4f * relativeVel.sqrMagnitude - 4f * maxProjectileSpeed * maxProjectileSpeed;

                float discriminant = b * b - 4f * a * c;

                if (discriminant < 0f) {
                    // No real solution
                    projectileDir = Vector3.zero;
                    projectileSpeed = 0f;
                    interceptT = -1f;
                    altT = -1f;
                    return false;
                }

                interceptT = (-b + Mathf.Sqrt(discriminant)) / (2f * a);
                    if (interceptT <= 0) interceptT = (-b - Mathf.Sqrt(discriminant)) / (2f * a);

                Vector3 interceptPos = targetInitPos + targetConstVel * interceptT;
                projectileDir = (interceptPos - projectilePos - 0.5f * interceptT * interceptT * gravity).normalized;
                projectileSpeed = maxProjectileSpeed;
                altT = interceptT;
                return true;
            }

            
            // With gravity - use iterative prediction for moving targets           
            
            const int maxIterations = 10;
            bool solutionFound = false;
            float bestError = float.MaxValue;

            float initialGuessT  = (targetInitPos - projectilePos).magnitude / maxProjectileSpeed;
            float timeGuess = Mathf.Max(initialGuessT, 0.01f);
            
            for (int i = 0; i < maxIterations; i++)
            {
                Vector3 predictedTarget = targetInitPos + targetConstVel * timeGuess;
                Vector3 toTarget = predictedTarget - projectilePos;

                Vector3 requiredVelocity = (toTarget - 0.5f * timeGuess * timeGuess * gravity) / timeGuess;
                float speed = requiredVelocity.magnitude;
                if (speed > maxProjectileSpeed)
                {
                    timeGuess *= 1.1f;
                    continue;
                }

                Vector3 projectileHit = projectilePos + requiredVelocity * timeGuess + 0.5f * gravity * timeGuess * timeGuess;
                Vector3 targetHit = targetInitPos + targetConstVel * timeGuess;
                float error = Vector3.Distance(projectileHit, targetHit);
                float dynamicAllowedError = Mathf.Lerp(0.8f, maxAllowedErrorDist, Mathf.Clamp01(timeGuess / maxInterceptTime));

                if (error < dynamicAllowedError)
                {
                    projectileDir = requiredVelocity.normalized;
                    projectileSpeed = speed * holdbackFactor;
                    interceptT = timeGuess;
                    altT = timeGuess;
                    return true;
                }

                if (error < bestError)
                {
                    bestError = error;
                    projectileDir = requiredVelocity.normalized;
                    projectileSpeed = speed;
                    interceptT = timeGuess;
                    altT = timeGuess;
                    solutionFound = true;
                }

                float horizontalDist = new Vector3(toTarget.x, 0, toTarget.z).magnitude;
                float horizontalSpeed = new Vector3(requiredVelocity.x, 0, requiredVelocity.z).magnitude;

                if (horizontalSpeed > 0.01f)
                    timeGuess = Mathf.Lerp(timeGuess, horizontalDist / horizontalSpeed, 0.5f);
                else
                    timeGuess += 0.05f;
            }

            if (solutionFound && interceptT <= maxInterceptTime)
            {
                return true;
            }

            return false;
        }

    }

}