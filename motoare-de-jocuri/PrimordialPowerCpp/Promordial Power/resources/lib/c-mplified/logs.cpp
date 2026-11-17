// LOG class //

#include "logs.hpp"

// LOG
Log::Log()
{
    level = 0;
}

//----------------------------------------------
// ERROR_LOG
void Log::errorLogSimple(std::string errMessage)
{
    if (level <= 2)
    {
        std::cout << "\033[K\033[31m\033[1m" << "Error :: \033[0m\033[31m" << errMessage << "\033[0m\n\n";
    }
}

void Log::errorLogFile(std::string errMessage, const char* file)
{
    if (level <= 2)
    {
        std::cout << "\033[K\033[31m\033[1m" << "Error :: \033[0m\033[31m" << errMessage << "\n";
        std::cout << "\033[31m| File: \033[0m\033[4m" << file << "\033[0m\n\n";
    }
}

void Log::errorLogSpecific(std::string errMessage, const char* file, int line)
{
    if (level <= 2)
    {
        std::cout << "\033[K\033[31m\033[1m" << "Error :: \033[0m\033[31m" << errMessage << "\n";
        std::cout << "\033[31m| File: \033[0m\033[4m" << file << "\n\033[0m";
        std::cout << "\033[31m| Line: \033[0m" << line << "\n\n";
    }
}

//----------------------------------------------
// WARNING_LOG
void Log::warningLogSimple(std::string warMessage)
{
    if (level <= 1)
    {
        std::cout << "\033[K\033[33m\033[1m" << "Warning :: \033[0m\033[33m" << warMessage << "\033[0m\n\n";
    }
}

void Log::warningLogFile(std::string warMessage, const char* file)
{
    if (level <= 1)
    {
        std::cout << "\033[K\033[33m\033[1m" << "Warning :: \033[0m\033[33m" << warMessage << "\n";
        std::cout << "\033[33m| File: \033[0m\033[4m" << file << "\033[0m\n\n";
    }
}

void Log::warningLogSpecific(std::string warMessage, const char* file, int line)
{
    if (level <= 1)
    {
        std::cout << "\033[K\033[33m\033[1m" << "Warning :: \033[0m\033[33m" << warMessage << "\n";
        std::cout << "\033[33m| File: \033[0m\033[4m" << file << "\n\033[0m";
        std::cout << "\033[33m| Line: \033[0m" << line << "\n\n";
    }
}

//----------------------------------------------
// INFO_LOG
void Log::infoLogSimple(std::string infMessage)
{
    if (level <= 0)
    {
        std::cout << "\033[K\033[36m\033[1m" << "Info :: \033[0m\033[36m" << infMessage << "\033[0m\n\n";
    }
}

void Log::infoLogFile(std::string infMessage, const char* file)
{
    if (level <= 0)
    {
        std::cout << "\033[K\033[36m\033[1m" << "Info :: \033[0m\033[36m" << infMessage << "\n";
        std::cout << "\033[36m| File: \033[0m\033[4m" << file << "\033[0m\n\n";
    }
}

void Log::infoLogSpecific(std::string infMessage, const char* file, int line)
{
    if (level <= 0)
    {
        std::cout << "\033[K\033[36m\033[1m" << "Info :: \033[0m\033[36m" << infMessage << "\n";
        std::cout << "\033[36m| File: \033[0m\033[4m" << file << "\n\033[0m";
        std::cout << "\033[36m| Line: \033[0m" << line << "\n\n";
    }
}

//----------------------------------------------
// PROGRESS_LOG
void Log::progressLog(std::string statusMessage, const float progress)
{
    std::cout << "\033[K\033[32m" << statusMessage << "\033[0m\n" << "[";

    for (unsigned int i = 1; i <= 20; i++)
    {
        // Progressing
        if (progress * 20 >= i)
            std::cout << "\033[32m=\033[0m";
        else
            std::cout << "\033[37m.\033[0m";
    }
    // Done?
    if (progress >= 1)
        std::cout << "]" << "\n\n";
    else
        std::cout << "]" << "\033[A\r";
}