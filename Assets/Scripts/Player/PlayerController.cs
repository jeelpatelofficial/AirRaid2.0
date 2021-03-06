﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public enum Abilities { Turrets, Rocket, Bomb }
public enum BodyPart { BodyPart_Turret, BodyPart_WingSlots, BodyPart_BombBay, BodyPart_FrontCannon }  //These enums tags must EXCATLY match the tag names
public class PlayerController : MonoBehaviour, IHittable
{
    public static readonly int ABILITY_COUNT_MAX = 6; //max number of abilites, to change this number, you would have to add more Axis in Editor->InputManager and UI ability parent grid column count

    //[HideInInspector] 
    //public bool stalled = false;
    public int hit  = 0;
    public bool isAlive;

    [HideInInspector] public PlayerStats stats;
    [HideInInspector] public Rigidbody rb;
    [HideInInspector] public Camera playerCam;
    [HideInInspector] public GameObject Smoke;
    [HideInInspector]
    public AbilityManager abilityManager;
    public PlayerSounds playerSounds;

    public Dictionary<BodyPart, List<Vector3>> bodyParts; //This uses transforms on the player that uses the correct BodyPart_ tag, those parts are deleted after consumed, and thier localPosition is saved

    bool activateSmoke = true;

    public void Initialize()
    {
        //Create stats, add two starter abilities
        abilityManager = new AbilityManager(this);
        abilityManager.AddAbilities(new Ab_MachineGun(this), 0); //Not the best way of adding an ability, it's a little unstable since it's not coupled with the inputSystem (for key pressing purposes)
        abilityManager.AddAbilities(new Ab_BombDrop(this), 1);
        //but it's important that I test now that my ability system is all in place.

        stats = new PlayerStats(this);
        stats.abilities.Add(Abilities.Turrets);
        stats.abilities.Add(Abilities.Rocket);

        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; //using custom gravity
        rb.velocity = transform.forward * stats.maxSpeed * .7f;

        playerCam = GetComponentInChildren<Camera>();
        playerSounds = GameObject.FindGameObjectWithTag("Player").GetComponent<PlayerSounds>();
        

        //Link all body parts
        bodyParts = new Dictionary<BodyPart, List<Vector3>>();

        Transform[] allChildren = transform.GetComponentsInChildren<Transform>();
        HashSet<string> bodyPartEnums = new HashSet<string>(System.Enum.GetNames(typeof(BodyPart))); //The code to get a hashset of enums represented as strings
        for(int i = allChildren.Length - 1; i >= 0; i--)  //since i will be deleting elements from this array, need to backwards for loop
        { //grab each object who has the correct body part tag
            if(bodyPartEnums.Contains(allChildren[i].tag))  //if the child tag matches one of the bodypart tags
            {
                BodyPart bodyPart = (BodyPart)System.Enum.Parse(typeof(BodyPart), allChildren[i].tag);  //Syntax to change the body part from a string to an enum
                //So we found a body part, add it to the dictionary
                if(!bodyParts.ContainsKey(bodyPart))
                {  //very first entry, need to add it to the dictionary and create the list
                    bodyParts.Add(bodyPart, new List<Vector3>());
                }
                bodyParts[bodyPart].Add(allChildren[i].transform.localPosition);
                GameObject.Destroy(allChildren[i].gameObject); //Dont need the transform anymore, just wanted it's local position
            }
        }
        isAlive = true;

        Smoke = Resources.Load<GameObject>("Prefabs/Smoke/WhiteSmoke");
    }

    public void PostInitialize()
    {
        
    }

    public void Refresh(InputManager.InputPkg inputPkg)
    {
        if (!stats.engineStalled)
        {
            abilityManager.Refresh(inputPkg);
        }
        ModEnergy(stats.energyRegenPerSec * Time.deltaTime);
        //stats.currentEnegy = Mathf.Clamp(stats.currentEnegy + stats.energyRegenPerSec * Time.deltaTime,0,stats.maxEnergy);
        if (stats.hp <= 0)
            ShipDestroyed();
        //Debug.Log("Energy: " + stats.currentEnegy);

        if(stats.hp < 50 && activateSmoke)
        {
            activateSmoke = false;
            GameObject smokeGO = Instantiate(Smoke, transform.position, Quaternion.identity);
            smokeGO.transform.SetParent(this.transform);
        }

    }

    public void PhysicsRefresh(InputManager.InputPkg inputPkg)
    {
        if (!stats.engineStalled)
        {
            abilityManager.PhysicsRefresh(inputPkg);
            Throttle(inputPkg.throttleAmount);                                                  //increase or decrease speed based on holding down the throttle amount (-1 to 1)

            rb.angularVelocity = transform.TransformDirection(new Vector3(stats.pitchSpeed * inputPkg.dirPressed.y, stats.yawSpeed * inputPkg.yawPressed, stats.rollSpeed * inputPkg.dirPressed.x));

            //Rotate velocity towards aim?
            //Vector3 relativeVelocity = rb.velocity;
            rb.velocity = Vector3.RotateTowards(rb.velocity, transform.forward, 5, 0);
            // rb.velocity = transform.TransformDirection(relativeLocalVelocity);
        }
        else
        {
            transform.localEulerAngles = Vector3.RotateTowards(transform.localEulerAngles, new Vector3(90,0, transform.localEulerAngles.z), .4f *Time.fixedDeltaTime, 0);
            rb.angularVelocity += transform.TransformDirection(new Vector3(0, 0, .2f) * Time.fixedDeltaTime);
            rb.velocity = Vector3.RotateTowards(rb.velocity, transform.forward, 5, 0);
        }
    }

    private void Throttle(float deltaThrottle)  //-1 to 1
    {
        if (deltaThrottle > .1f)
        {
            rb.AddRelativeForce(Vector3.forward * deltaThrottle * stats.acceleration);
            ModEnergy(-stats.energyPerThrustSecond * Time.fixedDeltaTime);
        }

        //https://answers.unity.com/questions/193398/velocity-relative-to-local.html
        Vector3 relativeLocalVelocity = stats.relativeLocalVelo;

        //Debug.Log("foward velo: " + relativeLocalVelocity);
        if(relativeLocalVelocity.z > stats.maxSpeed || relativeLocalVelocity.z < stats.minSpeed)  //if our foward is greater than max speed
        {
            relativeLocalVelocity.z = Mathf.Clamp(relativeLocalVelocity.z, stats.minSpeed, stats.maxSpeed);
            rb.velocity = transform.TransformDirection(relativeLocalVelocity);
        }
        //consume energy based on speed
       // Debug.Log("RelativeLocalVeloZ: " + relativeLocalVelocity.z + ", stats.MaxSpeed: " + stats.maxSpeed + " stats.energySpeedCostThreshold: " + stats.energySpeedCostThreshold);

        
    }

    public void ModEnergy(float modBy)
    {
        bool engineWasStalled = stats.engineStalled;

        stats.currentEnegy = Mathf.Clamp(stats.currentEnegy + modBy, -Mathf.Infinity, stats.maxEnergy);

        //If the change in energy causes use to lose control, we want a min of -20 (2 seconds) of stalling
        if (!engineWasStalled && stats.engineStalled)
            stats.currentEnegy = Mathf.Clamp(stats.currentEnegy, stats.currentEnegy, -20);
        //Eventaully this would be the spot to cause the engine stall
    }

    private void OnCollisionEnter(Collision collision)
    {
        playerSounds.PlayDamage();
        //At this moment, any physical collision should destroy the ship
        ShipDestroyed();
    }

    private void ShipDestroyed()
    {
        PlayerManager.Instance.PlayerDied();
        isAlive = false;
    }

    public void HitByProjectile(float damage)
    {
        stats.hp -= damage;
        Debug.Log("took dmg: " + damage);
        playerSounds.PlayDamage();

    }

    public int ReturnHit()
    {
        return hit;
    }

    //I put the players data in a seperate data class so it's easy to pass to the UI, and for being able to save the game
    public class PlayerStats    
    {
        public PlayerController player;
        [Header("Energy")]
        public float maxEnergy = 100;
        public float currentEnegy = 100;
        public float energyRegenPerSec = 10;
        public float energyPerThrustSecond = 15;
        [Header("Player Movement")]
        public float maxSpeed = 40;
        public float minSpeed = 9;
        public float acceleration = 30;
        public float pitchSpeed = .6f;
        public float yawSpeed = .6f;
        public float rollSpeed = 3;
        public readonly float forwardSpeedAtWhichGravityIsCanceled = 10;
        [Header("Stats")]
        public float hp;
        public float maxHp = 100;
        public bool engineStalled { get { return currentEnegy <= 0; } }

        public Vector3 relativeLocalVelo { get { return player.transform.InverseTransformDirection(player.rb.velocity); ; } }  //returns velo in local co-rd, since rb.velo is world
        public List<Abilities> abilities = new List<Abilities>();

        public PlayerStats(PlayerController pc) { player = pc; currentEnegy = maxEnergy; hp = maxHp; }
    }
}
