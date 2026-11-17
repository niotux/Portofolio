// LOG class //
#pragma once

#include "iostream"
#include "string"

// LOG
class Log
{
public:
	Log();

	unsigned int level;

	//----------------------------------------------
	// ERROR_LOG [level >= 1]
	void errorLogSimple(std::string errMessage);

	void errorLogFile(std::string errMessage, const char* file);

	void errorLogSpecific(std::string errMessage, const char* file, int line);
#define errorLog(errMessage) errorLogSpecific(errMessage, __FILE__, __LINE__);

	//----------------------------------------------
	// WARNING_LOG [level >= 2]
	void warningLogSimple(std::string warMessage);

	void warningLogFile(std::string warMessage, const char* file);

	void warningLogSpecific(std::string warMessage, const char* file, int line);
#define warningLog(warMessage) warningLogSpecific(warMessage, __FILE__, __LINE__);

	//----------------------------------------------
	// WARNING_LOG [level >= 2]
	void infoLogSimple(std::string infMessage);

	void infoLogFile(std::string infMessage, const char* file);

	void infoLogSpecific(std::string infMessage, const char* file, int line);
#define infoLog(infMessage) infoLogSpecific(infMessage, __FILE__, __LINE__);

	//----------------------------------------------
	// POGRESS_LOG
	void progressLog(std::string status, const float progress);

};