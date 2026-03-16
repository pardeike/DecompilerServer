namespace NestedNoSymbolsTestLibrary;

public class OuterContainer
{
    public class NestedWorker
    {
        public int Compute(int value)
        {
            return value + 1;
        }
    }
}
