
void updateProgressBar(const std::string& status, const float progress)
{
    std::cout << "\033[K" << status << std::endl << "[";

    for (unsigned int i = 1; i <= 20; i++)
    {
        // Progressing
        if (progress * 2 >= i * 10)
            std::cout << "=";
        else
            std::cout << ".";
    }
    // Done?
    if (progress >= 100)
        std::cout << "]" << std::endl << std::endl;
    else
        std::cout << "]" << "\033[A\r";
}