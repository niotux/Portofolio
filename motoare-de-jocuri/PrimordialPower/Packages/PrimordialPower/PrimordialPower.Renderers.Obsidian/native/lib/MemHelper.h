class MemHelper
{
public:
  template <typename T>
  static void Delete(T *&ptr)
  {
    if (ptr == nullptr)
      return;

    delete ptr;
    ptr = nullptr;
  }

  template <typename T>
  static void DeleteArray(T *&ptr)
  {
    if (ptr == nullptr)
      return;

    delete[] ptr;
    ptr = nullptr;
  }
};