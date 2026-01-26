#include <windows.h>
#include <bcrypt.h>
#include <wincrypt.h>
#include <iostream>
#include <string>
#include <vector>

#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "crypt32.lib")

static std::string EscapeJson(const std::string& value) {
    std::string out;
    out.reserve(value.size());
    for (char ch : value) {
        switch (ch) {
        case '\\': out += "\\\\"; break;
        case '"': out += "\\\""; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default: out += ch; break;
        }
    }
    return out;
}

static std::string ExtractString(const std::string& json, const std::string& key) {
    std::string pattern = "\"" + key + "\"";
    auto keyPos = json.find(pattern);
    if (keyPos == std::string::npos) {
        return "";
    }
    auto colonPos = json.find(':', keyPos + pattern.size());
    if (colonPos == std::string::npos) {
        return "";
    }
    auto quotePos = json.find('"', colonPos + 1);
    if (quotePos == std::string::npos) {
        return "";
    }
    auto endPos = json.find('"', quotePos + 1);
    if (endPos == std::string::npos) {
        return "";
    }
    return json.substr(quotePos + 1, endPos - quotePos - 1);
}

static bool Base64Decode(const std::string& input, std::vector<unsigned char>& output) {
    DWORD outLen = 0;
    if (!CryptStringToBinaryA(input.c_str(), static_cast<DWORD>(input.size()), CRYPT_STRING_BASE64, nullptr, &outLen, nullptr, nullptr)) {
        return false;
    }
    output.resize(outLen);
    return CryptStringToBinaryA(input.c_str(), static_cast<DWORD>(input.size()), CRYPT_STRING_BASE64, output.data(), &outLen, nullptr, nullptr) != 0;
}

static bool Base64Encode(const std::vector<unsigned char>& input, std::string& output) {
    DWORD outLen = 0;
    if (!CryptBinaryToStringA(input.data(), static_cast<DWORD>(input.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, nullptr, &outLen)) {
        return false;
    }
    output.resize(outLen);
    if (!CryptBinaryToStringA(input.data(), static_cast<DWORD>(input.size()), CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, output.data(), &outLen)) {
        return false;
    }
    if (!output.empty() && output.back() == '\0') {
        output.pop_back();
    }
    return true;
}

static bool DeriveKey(const std::string& keyId, std::vector<unsigned char>& key, std::string& error) {
    std::string material = "LNM|" + keyId + "|v1";

    BCRYPT_ALG_HANDLE hashAlg = nullptr;
    BCRYPT_HASH_HANDLE hashHandle = nullptr;
    DWORD hashObjectLength = 0;
    DWORD dataLength = 0;
    DWORD hashLength = 0;

    NTSTATUS status = BCryptOpenAlgorithmProvider(&hashAlg, BCRYPT_SHA256_ALGORITHM, nullptr, 0);
    if (status != 0) {
        error = "Hash algoritmasi acilamadi.";
        return false;
    }

    status = BCryptGetProperty(hashAlg, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&hashObjectLength), sizeof(DWORD), &dataLength, 0);
    if (status != 0) {
        error = "Hash objesi alinmadi.";
        BCryptCloseAlgorithmProvider(hashAlg, 0);
        return false;
    }

    status = BCryptGetProperty(hashAlg, BCRYPT_HASH_LENGTH, reinterpret_cast<PUCHAR>(&hashLength), sizeof(DWORD), &dataLength, 0);
    if (status != 0) {
        error = "Hash uzunlugu alinmadi.";
        BCryptCloseAlgorithmProvider(hashAlg, 0);
        return false;
    }

    std::vector<unsigned char> hashObject(hashObjectLength);
    key.assign(hashLength, 0);

    status = BCryptCreateHash(hashAlg, &hashHandle, hashObject.data(), hashObjectLength, nullptr, 0, 0);
    if (status != 0) {
        error = "Hash olusturulamadi.";
        BCryptCloseAlgorithmProvider(hashAlg, 0);
        return false;
    }

    status = BCryptHashData(hashHandle, reinterpret_cast<PUCHAR>(const_cast<char*>(material.data())), static_cast<ULONG>(material.size()), 0);
    if (status != 0) {
        error = "Hash verisi islenemedi.";
        BCryptDestroyHash(hashHandle);
        BCryptCloseAlgorithmProvider(hashAlg, 0);
        return false;
    }

    status = BCryptFinishHash(hashHandle, key.data(), static_cast<ULONG>(key.size()), 0);
    BCryptDestroyHash(hashHandle);
    BCryptCloseAlgorithmProvider(hashAlg, 0);
    if (status != 0) {
        error = "Hash tamamlama hatasi.";
        return false;
    }

    return true;
}

static bool EncryptPayload(const std::string& keyId, const std::string& payloadBase64, std::string& outputBase64, std::string& error) {
    std::vector<unsigned char> plainBytes;
    if (!payloadBase64.empty() && !Base64Decode(payloadBase64, plainBytes)) {
        error = "Base64 cozulemedi.";
        return false;
    }

    std::vector<unsigned char> key;
    if (!DeriveKey(keyId, key, error)) {
        return false;
    }

    BCRYPT_ALG_HANDLE aesAlg = nullptr;
    BCRYPT_KEY_HANDLE keyHandle = nullptr;
    DWORD keyObjectLength = 0;
    DWORD dataLength = 0;
    DWORD blockLength = 0;

    NTSTATUS status = BCryptOpenAlgorithmProvider(&aesAlg, BCRYPT_AES_ALGORITHM, nullptr, 0);
    if (status != 0) {
        error = "AES algoritmasi acilamadi.";
        return false;
    }

    status = BCryptSetProperty(aesAlg, BCRYPT_CHAINING_MODE, reinterpret_cast<PUCHAR>(const_cast<wchar_t*>(BCRYPT_CHAIN_MODE_CBC)),
        static_cast<ULONG>(wcslen(BCRYPT_CHAIN_MODE_CBC) * sizeof(wchar_t)), 0);
    if (status != 0) {
        error = "CBC modu ayarlanamadi.";
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    status = BCryptGetProperty(aesAlg, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&keyObjectLength), sizeof(DWORD), &dataLength, 0);
    if (status != 0) {
        error = "Key objesi alinmadi.";
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    status = BCryptGetProperty(aesAlg, BCRYPT_BLOCK_LENGTH, reinterpret_cast<PUCHAR>(&blockLength), sizeof(DWORD), &dataLength, 0);
    if (status != 0 || blockLength == 0) {
        error = "Blok uzunlugu alinmadi.";
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    std::vector<unsigned char> keyObject(keyObjectLength);
    status = BCryptGenerateSymmetricKey(aesAlg, &keyHandle, keyObject.data(), keyObjectLength, key.data(), static_cast<ULONG>(key.size()), 0);
    if (status != 0) {
        error = "Key olusturulamadi.";
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    std::vector<unsigned char> iv(blockLength);
    if (BCryptGenRandom(nullptr, iv.data(), blockLength, BCRYPT_USE_SYSTEM_PREFERRED_RNG) != 0) {
        error = "IV olusturulamadi.";
        BCryptDestroyKey(keyHandle);
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    std::vector<unsigned char> ivWork = iv;
    ULONG cipherLength = 0;
    status = BCryptEncrypt(keyHandle,
        plainBytes.empty() ? nullptr : plainBytes.data(),
        static_cast<ULONG>(plainBytes.size()),
        nullptr,
        ivWork.data(),
        static_cast<ULONG>(ivWork.size()),
        nullptr,
        0,
        &cipherLength,
        BCRYPT_BLOCK_PADDING);

    if (status != 0) {
        error = "Sifreleme uzunlugu alinmadi.";
        BCryptDestroyKey(keyHandle);
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    std::vector<unsigned char> cipher(cipherLength);
    ivWork = iv;
    status = BCryptEncrypt(keyHandle,
        plainBytes.empty() ? nullptr : plainBytes.data(),
        static_cast<ULONG>(plainBytes.size()),
        nullptr,
        ivWork.data(),
        static_cast<ULONG>(ivWork.size()),
        cipher.data(),
        cipherLength,
        &cipherLength,
        BCRYPT_BLOCK_PADDING);

    BCryptDestroyKey(keyHandle);
    BCryptCloseAlgorithmProvider(aesAlg, 0);

    if (status != 0) {
        error = "Sifreleme hatasi.";
        return false;
    }

    cipher.resize(cipherLength);
    std::vector<unsigned char> combined;
    combined.reserve(iv.size() + cipher.size());
    combined.insert(combined.end(), iv.begin(), iv.end());
    combined.insert(combined.end(), cipher.begin(), cipher.end());

    if (!Base64Encode(combined, outputBase64)) {
        error = "Base64 uretilemedi.";
        return false;
    }

    return true;
}

static bool DecryptPayload(const std::string& keyId, const std::string& payloadBase64, std::string& outputBase64, std::string& error) {
    std::vector<unsigned char> combined;
    if (!Base64Decode(payloadBase64, combined)) {
        error = "Base64 cozulemedi.";
        return false;
    }

    if (combined.size() < 16) {
        error = "Sifreli veri gecersiz.";
        return false;
    }

    std::vector<unsigned char> key;
    if (!DeriveKey(keyId, key, error)) {
        return false;
    }

    BCRYPT_ALG_HANDLE aesAlg = nullptr;
    BCRYPT_KEY_HANDLE keyHandle = nullptr;
    DWORD keyObjectLength = 0;
    DWORD dataLength = 0;
    DWORD blockLength = 0;

    NTSTATUS status = BCryptOpenAlgorithmProvider(&aesAlg, BCRYPT_AES_ALGORITHM, nullptr, 0);
    if (status != 0) {
        error = "AES algoritmasi acilamadi.";
        return false;
    }

    status = BCryptSetProperty(aesAlg, BCRYPT_CHAINING_MODE, reinterpret_cast<PUCHAR>(const_cast<wchar_t*>(BCRYPT_CHAIN_MODE_CBC)),
        static_cast<ULONG>(wcslen(BCRYPT_CHAIN_MODE_CBC) * sizeof(wchar_t)), 0);
    if (status != 0) {
        error = "CBC modu ayarlanamadi.";
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    status = BCryptGetProperty(aesAlg, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&keyObjectLength), sizeof(DWORD), &dataLength, 0);
    if (status != 0) {
        error = "Key objesi alinmadi.";
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    status = BCryptGetProperty(aesAlg, BCRYPT_BLOCK_LENGTH, reinterpret_cast<PUCHAR>(&blockLength), sizeof(DWORD), &dataLength, 0);
    if (status != 0 || blockLength == 0) {
        error = "Blok uzunlugu alinmadi.";
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    std::vector<unsigned char> keyObject(keyObjectLength);
    status = BCryptGenerateSymmetricKey(aesAlg, &keyHandle, keyObject.data(), keyObjectLength, key.data(), static_cast<ULONG>(key.size()), 0);
    if (status != 0) {
        error = "Key olusturulamadi.";
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    std::vector<unsigned char> iv(combined.begin(), combined.begin() + blockLength);
    std::vector<unsigned char> cipher(combined.begin() + blockLength, combined.end());

    std::vector<unsigned char> ivWork = iv;
    ULONG plainLength = 0;
    status = BCryptDecrypt(keyHandle,
        cipher.data(),
        static_cast<ULONG>(cipher.size()),
        nullptr,
        ivWork.data(),
        static_cast<ULONG>(ivWork.size()),
        nullptr,
        0,
        &plainLength,
        BCRYPT_BLOCK_PADDING);

    if (status != 0) {
        error = "Cozme uzunlugu alinmadi.";
        BCryptDestroyKey(keyHandle);
        BCryptCloseAlgorithmProvider(aesAlg, 0);
        return false;
    }

    std::vector<unsigned char> plain(plainLength);
    ivWork = iv;
    status = BCryptDecrypt(keyHandle,
        cipher.data(),
        static_cast<ULONG>(cipher.size()),
        nullptr,
        ivWork.data(),
        static_cast<ULONG>(ivWork.size()),
        plain.data(),
        plainLength,
        &plainLength,
        BCRYPT_BLOCK_PADDING);

    BCryptDestroyKey(keyHandle);
    BCryptCloseAlgorithmProvider(aesAlg, 0);

    if (status != 0) {
        error = "Cozme hatasi.";
        return false;
    }

    plain.resize(plainLength);
    if (!Base64Encode(plain, outputBase64)) {
        error = "Base64 uretilemedi.";
        return false;
    }

    return true;
}

static std::string BuildResponse(const std::string& id, const std::string& payloadBase64, const std::string& errorMessage) {
    std::string json = "{\"id\":\"" + EscapeJson(id) + "\",\"type\":\"crypto.response\",\"payload\":{";
    if (errorMessage.empty()) {
        json += "\"payloadBase64\":\"" + EscapeJson(payloadBase64) + "\",\"error\":null";
    } else {
        json += "\"payloadBase64\":\"" + EscapeJson(payloadBase64) + "\",\"error\":{\"code\":\"CRYPTO_ERROR\",\"message\":\"" + EscapeJson(errorMessage) + "\"}";
    }
    json += "}}";
    return json;
}

int main() {
    std::ios::sync_with_stdio(false);
    std::cin.tie(nullptr);

    std::string line;
    while (std::getline(std::cin, line)) {
        if (line.empty()) {
            continue;
        }

        std::string id = ExtractString(line, "id");
        if (id.empty()) {
            continue;
        }

        std::string operation = ExtractString(line, "operation");
        std::string keyId = ExtractString(line, "keyId");
        std::string payloadBase64 = ExtractString(line, "payloadBase64");

        if (keyId.empty()) {
            keyId = "default";
        }

        std::string outputBase64;
        std::string error;

        if (operation == "encrypt") {
            if (!EncryptPayload(keyId, payloadBase64, outputBase64, error)) {
                std::cout << BuildResponse(id, payloadBase64, error) << std::endl;
                continue;
            }
        } else if (operation == "decrypt") {
            if (!DecryptPayload(keyId, payloadBase64, outputBase64, error)) {
                std::cout << BuildResponse(id, payloadBase64, error) << std::endl;
                continue;
            }
        } else {
            std::cout << BuildResponse(id, payloadBase64, "Gecersiz istek.") << std::endl;
            continue;
        }

        std::cout << BuildResponse(id, outputBase64, "") << std::endl;
    }

    return 0;
}
