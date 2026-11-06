using System;

internal sealed class EnemyAggro<T> where T : class
{
	private readonly float _switchCooldown;
	private readonly Func<T, bool> _isEligible;
	private T _pendingTarget;
	private float _nextSwitchTime;

	public T Target { get; private set; }

	public EnemyAggro(float switchCooldown, Func<T, bool> isEligible)
	{
		_switchCooldown = switchCooldown;
		_isEligible = isEligible;
	}

	public void Advance(float now)
	{
		if (Target != null && !_isEligible(Target))
		{
			Target = null;
		}

		if (_pendingTarget != null && !_isEligible(_pendingTarget))
		{
			_pendingTarget = null;
		}

		if (_pendingTarget != null && (Target == null || now >= _nextSwitchTime))
		{
			SwitchTarget(_pendingTarget, now);
		}
	}

	public void RecordHit(T shooter, float now)
	{
		Advance(now);
		if (ReferenceEquals(Target, shooter))
		{
			return;
		}

		if (Target == null || now >= _nextSwitchTime)
		{
			SwitchTarget(shooter, now);
			return;
		}

		_pendingTarget = shooter;
	}

	private void SwitchTarget(T target, float now)
	{
		Target = target;
		_pendingTarget = null;
		_nextSwitchTime = now + _switchCooldown;
	}
}
