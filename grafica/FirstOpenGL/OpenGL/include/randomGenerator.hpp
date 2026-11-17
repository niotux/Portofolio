float* rnd(unsigned int seed, unsigned int arrSize)
{
    float* arr = new float[arrSize];
    const unsigned int r = 1 << 24;

    for (unsigned int i = 0; i < arrSize; i++)
    {
        seed = (1664525 * seed + 1013904223) % r;
        arr[i] = (float)seed / (float)r;
    }
    return arr;
}