using System;
using Starbelter.Core;
using Starbelter.Space;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class ShieldController : MonoBehaviour
{
    private const string SPACE_WEAPON_TAG = "SpaceWeapon";

    private float rechargeRate = 3f;
    private float naturalDrain = 1.5f;
    private float maxShield = 100f;
    private float currentShield = 100f;
    private float optimalPowerRequired = 100;
    private float powerDelegated = 150;
    private float efficiencyBonus = 0f;

    private float tickTimer = 0f;
    private const float TICK_INTERVAL = 1f;

    private Collider2D shieldCollider;

    private ShipController shipController;

    [SerializeField] private SpriteRenderer shieldSprite;
    [SerializeField] private float pulseSpeed = 2f;

    private const float BASE_ALPHA = 25f / 255f;
    private const float PULSE_RANGE = 5f / 255f;

    void Update()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer >= TICK_INTERVAL)
        {
            tickTimer -= TICK_INTERVAL;
            Tick();
        }

        UpdateShieldVisual();
    }

    void UpdateShieldVisual()
    {
        if (shieldSprite == null)
            return;

        float shieldPercent = currentShield / maxShield;
        float scaledBaseAlpha = BASE_ALPHA * shieldPercent;
        float scaledPulseRange = PULSE_RANGE * shieldPercent;

        // Ping-pong between 0 and 1, then scale to pulse range
        float pulse = Mathf.PingPong(Time.time * pulseSpeed, 1f) * scaledPulseRange;
        float alpha = scaledBaseAlpha + pulse;

        Color color = shieldSprite.color;
        color.a = alpha;
        shieldSprite.color = color;
    }

    void TakeDamage(DamagePacket packet)
    {
        float damage = packet.Damage;

        // Apply damage type modifiers
        switch (packet.Type)
        {
            case DamageType.Physical:
                damage *= 0.5f; // Shields resist kinetic
                break;
            case DamageType.Heat:
                damage *= 1.0f; // Normal
                break;
            case DamageType.Energy:
                damage *= 1.25f; // Shields weak to energy
                break;
            case DamageType.Ion:
                damage *= 2.0f; // Shields very weak to ion
                break;
        }

        // Apply damage to shield
        float overflow = damage - currentShield;
        currentShield = Mathf.Max(0, currentShield - damage);

        

        if (overflow > 0)
        {
            // shipController.TakeHullDamage(overflow, packet);
        }
        else
        {
            bool scripted = packet.Source.TryGetComponent(out SpaceProjectile projectile);
            if (scripted)
            {
               projectile.OnImpact(); 
            }
            //shipController.TakeShieldHit(); 
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag(SPACE_WEAPON_TAG))
            return;

        var weapon = collision.GetComponent<ISpaceWeapon>();
        if (weapon == null)
            return;

        Vector2 hitPoint = collision.ClosestPoint(transform.position);

        var packet = new DamagePacket(
            damage: weapon.Damage,
            type: weapon.DamageType,
            hitPoint: hitPoint,
            origin: weapon.Origin,
            source: collision.gameObject
        );

        TakeDamage(packet);
    }

    void Start()
    {
        shieldCollider = GetComponent<Collider2D>();
        shipController = transform.root.GetComponent<ShipController>();
    }

    private void Tick()
    {
        float overloadedMaxShield = maxShield + CalculateOverload();
        float deltaShield = (rechargeRate * CalculateEfficiency()) - naturalDrain;
        currentShield = Mathf.Clamp(currentShield + deltaShield, 0, overloadedMaxShield);
        Debug.Log(currentShield);
    }


    private float CalculateOverload()
    {
        float ratio = powerDelegated / optimalPowerRequired;
        if(ratio <= 1f)
            return 0;

        return maxShield * ratio / 2;
    }
    private float CalculateEfficiency()
    {
        float ratio = powerDelegated / optimalPowerRequired;
        if (ratio > 1f)
        {
            float overage = ratio - 1f;
            ratio = 1f + Mathf.Pow(Mathf.Log(1f + overage), 0.5f);
        }
        float efficiency = ratio + efficiencyBonus;
        return efficiency;
    }
}
