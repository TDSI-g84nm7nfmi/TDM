#include <Python.h>
#include <string>
#include <vector>
#include <fstream>
#include <iostream>
#include <thread>
#include <chrono>
#include <filesystem>

namespace fs = std::filesystem;

// 文件信息结构
struct FileInfo {
    std::string path;
    size_t size;
    std::string hash;
    std::string last_modified;
};

// 下载进度回调
typedef void (*ProgressCallback)(size_t downloaded, size_t total, const char* filename);

// 全局变量
static PyObject* g_progress_callback = nullptr;

// 工具函数
std::string calculate_file_hash(const std::string& filepath) {
    std::ifstream file(filepath, std::ios::binary);
    if (!file.is_open()) {
        return "";
    }
    
    // 简单的哈希计算（实际项目中应使用更安全的哈希算法）
    size_t hash = 0;
    char buffer[4096];
    while (file.read(buffer, sizeof(buffer))) {
        for (size_t i = 0; i < file.gcount(); ++i) {
            hash = hash * 31 + buffer[i];
        }
    }
    
    return std::to_string(hash);
}

// Python模块函数
static PyObject* calculate_file_hash_py(PyObject* self, PyObject* args) {
    const char* filepath;
    if (!PyArg_ParseTuple(args, "s", &filepath)) {
        return nullptr;
    }
    
    std::string hash = calculate_file_hash(filepath);
    return PyUnicode_FromString(hash.c_str());
}

static PyObject* get_file_info_py(PyObject* self, PyObject* args) {
    const char* filepath;
    if (!PyArg_ParseTuple(args, "s", &filepath)) {
        return nullptr;
    }
    
    try {
        fs::path path(filepath);
        if (!fs::exists(path)) {
            PyErr_SetString(PyExc_FileNotFoundError, "File not found");
            return nullptr;
        }
        
        FileInfo info;
        info.path = filepath;
        info.size = fs::file_size(path);
        info.hash = calculate_file_hash(filepath);
        
        auto time = fs::last_write_time(path);
        auto sctp = std::chrono::time_point_cast<std::chrono::system_clock::duration>(
            time - fs::file_time_type::clock::now() + std::chrono::system_clock::now());
        auto cftime = std::chrono::system_clock::to_time_t(sctp);
        info.last_modified = std::ctime(&cftime);
        
        // 创建Python字典
        PyObject* dict = PyDict_New();
        PyDict_SetItemString(dict, "path", PyUnicode_FromString(info.path.c_str()));
        PyDict_SetItemString(dict, "size", PyLong_FromSize_t(info.size));
        PyDict_SetItemString(dict, "hash", PyUnicode_FromString(info.hash.c_str()));
        PyDict_SetItemString(dict, "last_modified", PyUnicode_FromString(info.last_modified.c_str()));
        
        return dict;
    } catch (const std::exception& e) {
        PyErr_SetString(PyExc_RuntimeError, e.what());
        return nullptr;
    }
}

static PyObject* fast_file_copy_py(PyObject* self, PyObject* args) {
    const char* src_path;
    const char* dst_path;
    if (!PyArg_ParseTuple(args, "ss", &src_path, &dst_path)) {
        return nullptr;
    }
    
    try {
        fs::path src(src_path);
        fs::path dst(dst_path);
        
        if (!fs::exists(src)) {
            PyErr_SetString(PyExc_FileNotFoundError, "Source file not found");
            return nullptr;
        }
        
        // 创建目标目录
        fs::create_directories(dst.parent_path());
        
        // 复制文件
        fs::copy_file(src, dst, fs::copy_options::overwrite_existing);
        
        Py_RETURN_TRUE;
    } catch (const std::exception& e) {
        PyErr_SetString(PyExc_RuntimeError, e.what());
        return nullptr;
    }
}

static PyObject* scan_directory_py(PyObject* self, PyObject* args) {
    const char* dir_path;
    if (!PyArg_ParseTuple(args, "s", &dir_path)) {
        return nullptr;
    }
    
    try {
        fs::path path(dir_path);
        if (!fs::exists(path) || !fs::is_directory(path)) {
            PyErr_SetString(PyExc_FileNotFoundError, "Directory not found");
            return nullptr;
        }
        
        PyObject* file_list = PyList_New(0);
        
        for (const auto& entry : fs::recursive_directory_iterator(path)) {
            if (fs::is_regular_file(entry)) {
                PyObject* file_dict = PyDict_New();
                PyDict_SetItemString(file_dict, "path", PyUnicode_FromString(entry.path().string().c_str()));
                PyDict_SetItemString(file_dict, "size", PyLong_FromSize_t(fs::file_size(entry)));
                PyDict_SetItemString(file_dict, "name", PyUnicode_FromString(entry.path().filename().string().c_str()));
                
                PyList_Append(file_list, file_dict);
                Py_DECREF(file_dict);
            }
        }
        
        return file_list;
    } catch (const std::exception& e) {
        PyErr_SetString(PyExc_RuntimeError, e.what());
        return nullptr;
    }
}

// 方法定义
static PyMethodDef NativeModuleMethods[] = {
    {"calculate_file_hash", calculate_file_hash_py, METH_VARARGS, "Calculate file hash"},
    {"get_file_info", get_file_info_py, METH_VARARGS, "Get file information"},
    {"fast_file_copy", fast_file_copy_py, METH_VARARGS, "Fast file copy"},
    {"scan_directory", scan_directory_py, METH_VARARGS, "Scan directory recursively"},
    {nullptr, nullptr, 0, nullptr}
};

// 模块定义
static struct PyModuleDef native_module = {
    PyModuleDef_HEAD_INIT,
    "native_module",
    "Native C++ module for high-performance file operations",
    -1,
    NativeModuleMethods
};

// 模块初始化函数
PyMODINIT_FUNC PyInit_native_module(void) {
    return PyModule_Create(&native_module);
} 