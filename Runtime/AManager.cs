public abstract class AManager<T> : IManager where T:new()
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new T();
            }
            return _instance;
        }
    }
    
    public virtual void Initialize() { }

    public virtual void OnRelease() { }
    
}