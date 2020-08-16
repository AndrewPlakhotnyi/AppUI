namespace BlazorDesktop {
    internal class Lock<T> {
        public Lock(T value) => Value = value;
        public T Value { get; set; }

        public void Set(T value) => Value = value;
    }
}
