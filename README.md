
<div align="center">

# 🔒 Local Network Messenger

**Yerel ağ (LAN) üzerinde çalışan, merkeziyetsiz ve ultra yüksek güvenlikli iletişim platformu**

Mesajlaşma • Sesli/Görüntülü Arama • Ekran Paylaşımı • Dosya Transferi

[![Platform](https://img.shields.io/badge/platform-Windows-blue)](#)
[![License](https://img.shields.io/badge/license-MIT-green)](#-lisans)
[![Made with C%23](https://img.shields.io/badge/C%23-.NET-purple)](#)
[![Made with C++](https://img.shields.io/badge/C++-Encryption--Core-orange)](#)
[![Made with Python](https://img.shields.io/badge/Python-Threat--Analysis-yellow)](#)

</div>

---

## 📖 İçindekiler

- [Hakkında](#-hakkında)
- [Temel Özellikler](#-temel-özellikler)
- [Mimari ve Teknolojik Yapı](#️-mimari-ve-teknolojik-yapı)
- [Kurulum ve Çalıştırma](#-kurulum-ve-çalıştırma)
- [Katkıda Bulunma](#-katkıda-bulunma)
- [Lisans](#-lisans)

---

## 📌 Hakkında

**Local Network Messenger**, yerel ağlar (LAN) üzerinde çalışan; ultra yüksek güvenlikli mesajlaşma, sesli/görüntülü arama, ekran paylaşımı ve dosya transferi sunan merkeziyetsiz bir iletişim platformudur.

Tamamen yerel ortamda çalışarak dış sunuculara olan bağımlılığı ortadan kaldırır. C++ ile geliştirilmiş özel şifreleme çekirdeği ve Python tabanlı anlık ağ tehdit analiz motoru sayesinde ağınızdaki veri trafiğini maksimum güvenlik altında tutar.

---

## ✨ Temel Özellikler

| Özellik | Açıklama |
|---|---|
| 🔐 **C++ Şifreleme Motoru** | Mesajlar, ses ve dosya transferleri C++ ile yazılmış üst düzey şifreleme algoritmalarıyla korunur |
| 🛡️ **Python Ağ & Tehdit Analizi** | Yerel ağ trafiğini anlık izler, yetkisiz erişim veya güvenlik tehditlerini anında tespit eder |
| 💬 **Uçtan Uca Şifreli Sohbet** | Sunucusuz mimari ile mesajlar doğrudan cihazlar arasında şifrelenerek iletilir |
| 📞 **Sesli & Görüntülü Arama** | Yerel ağ bant genişliğini kullanarak düşük gecikmeli, kesintisiz iletişim sağlar |
| 🖥️ **Ekran Paylaşımı** | Yerel ağ üzerinden yüksek kalitede ve güvenli ekran yayını |
| 📁 **Hızlı & Güvenli Dosya Paylaşımı** | Boyut sınırı olmadan doğrudan cihazlar arası (P2P) şifreli dosya aktarımı |
| 🎨 **Modern ve Dinamik Arayüz** | C# altyapısı üzerinde HTML, CSS ve JavaScript ile tasarlanmış şık, sezgisel arayüz |

---

## 🏗️ Mimari ve Teknolojik Yapı

Bu proje, farklı dillerin en güçlü yönleri birleştirilerek hibrit bir mimariyle tasarlanmıştır:

```
┌─────────────────────────────────────────────┐
│           HTML5 / CSS3 / JavaScript          │  → Kullanıcı arayüzü (UI)
├─────────────────────────────────────────────┤
│              C# (.NET Base)                  │  → Uygulama mimarisi, pencere yönetimi
├─────────────────────────────────────────────┤
│      C++ Encryption/Decryption Core          │  → Yüksek performanslı şifreleme çekirdeği
├─────────────────────────────────────────────┤
│   Python Ağ Analizi & Tehdit Tespit Sistemi  │  → Paket denetimi, arka plan analizi
└─────────────────────────────────────────────┘
```

| Katman | Teknoloji | Görev |
|---|---|---|
| Arayüz | HTML5 / CSS3 / JavaScript | Esnek, modern ve özelleştirilebilir UI |
| Uygulama Çekirdeği | C# (.NET) | Ana uygulama mimarisi ve arayüz entegrasyonu |
| Şifreleme | C++ | Yüksek performanslı veri şifreleme / şifre çözme |
| Ağ Güvenliği | Python | Anlık ağ analizi, paket denetimi, tehdit tespiti |

---

## 🚀 Kurulum ve Çalıştırma

### 1️⃣ Hazır Installer ile Kurulum *(Önerilen)*

Projeyi derlemekle uğraşmadan direkt kurup kullanmak isteyenler için:

1. Repodaki **`Installer/`** klasörüne gidin.
2. Kurulum dosyasını çalıştırarak uygulamayı bilgisayarınıza yükleyin.

### 2️⃣ Visual Studio ile Derleme *(Geliştiriciler İçin)*

Projeyi yerel ortamınızda geliştirmek ve derlemek isterseniz:

1. Bu depoyu klonlayın:

   ```bash
   git clone https://github.com/06eren/Local-Network-Messenger.git
   ```

2. Projeyi Visual Studio ile açın ve gerekli bağımlılıkları yükleyin.
3. Çözümü (solution) derleyin ve çalıştırın.

---

## 🤝 Katkıda Bulunma

Katkılarınızı bekliyoruz! Bir hata bulduysanız veya yeni bir özellik önermek istiyorsanız:

1. Bu depoyu fork'layın
2. Yeni bir dal (branch) oluşturun (`git checkout -b ozellik/yeni-ozellik`)
3. Değişikliklerinizi commit'leyin (`git commit -m 'Yeni özellik eklendi'`)
4. Dalınızı push'layın (`git push origin ozellik/yeni-ozellik`)
5. Bir Pull Request açın

---

## 📄 Lisans

Bu proje [MIT Lisansı](LICENSE) ile lisanslanmıştır — dilediğiniz gibi kullanabilir, değiştirebilir ve dağıtabilirsiniz.

---

<div align="center">

Yerel ağınızda güvenli iletişim için ❤️ ile geliştirildi.

</div>
