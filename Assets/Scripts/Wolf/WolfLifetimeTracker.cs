using UnityEngine;

public class WolfLifetimeTracker : MonoBehaviour
{
    private WolfSpawner _spawner;

    public void Init(WolfSpawner spawner)
    {
        _spawner = spawner;
    }

    private void OnDestroy()
    {
        if (_spawner != null)
        {
            _spawner.NotifyWolfDestroyed();
        }
    }
}