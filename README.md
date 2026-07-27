# 🔒 Local Network Messenger

**Local Network Messenger**, yerel ağlar (LAN) üzerinde çalışan; ultra yüksek güvenlikli mesajlaşma, sesli/görüntülü arama, ekran paylaşımı ve dosya transferi sunan merkeziyetsiz bir iletişim platformudur.

Tamamen yerel ortamda (local) çalışarak dış sunuculara olan bağımlılığı ortadan kaldırır. C++ ile geliştirilmiş özel şifreleme çekirdeği ve Python tabanlı anlık ağ tehdit analiz motoru sayesinde ağınızdaki veri trafiğini maksimum güvenlik altında tutar.

---

## ✨ Temel Özellikler

* 🔐 **C++ Şifreleme Engine:** Mesajlar, ses ve dosya transferleri C++ ile yazılmış üst düzey şifreleme algoritmalarıyla koruma altına alınır.
* 🛡️ **Python Ağ & Tehdit Analizi:** Yerel ağ trafiğini anlık olarak izler, yetkisiz erişim veya ağda oluşan güvenlik tehditlerini anında tespit eder.
* 💬 **Uçtan Uca Şifreli Sohbet:** Sunucusuz mimari ile mesajlar doğrudan cihazlar arasında şifrelenerek iletilir.
* 📞 **Sesli & Görüntülü Arama:** Yerel ağ bant genişliğini kullanarak düşük gecikmeli, kesintisiz iletişim sağlar.
* 🖥️ **Ekran Paylaşımı:** Yerel ağ üzerinden yüksek kalitede ve güvenli ekran yayını yapabilme imkanı.
* 📁 **Hızlı & Güvenli Dosya Paylaşımı:** Boyut sınırı olmadan doğrudan cihazlar arası (P2P mantığıyla) şifreli dosya aktarımı.
* 🎨 **Modern ve Dinamik Arayüz:** C# altyapısı üzerinde HTML, CSS ve JavaScript kullanılarak tasarlanmış şık ve sezgisel kullanıcı deneyimi.

---


## 🛠️ Mimari ve Teknolojik Yapı

Bu proje, farklı dillerin en güçlü yönleri birleştirilerek hibrit bir mimariyle tasarlanmıştır:

* **C# (.NET Base):** Ana uygulama mimarisi, pencere yönetimi ve arayüz entegrasyonu.
* **C++:** Yüksek performanslı veri şifreleme / şifre çözme (Encryption/Decryption) çekirdeği.
* **Python:** Arka planda çalışan anlık ağ analizi, paket denetimi ve tehdit tespit sistemi.
* **HTML5 / CSS3 / JavaScript:** Esnek, modern ve özelleştirilebilir kullanıcı arayüzü (UI).

---

## 🚀 Kurulum ve Çalıştırma

### 1. Hazır Installer İle Kurulum (Önerilen)
Projeyi derlemekle uğraşmadan direkt kurup kullanmak için:

1. Repodaki **`Installer/`** klasörüne gidin.
2. Kurulum dosyasını çalıştırarak uygulamayı bilgisayarınıza yükleyin.

### 2. Visual Studio İle Derleme (Geliştiriciler İçin)
Projeyi yerel ortamınızda geliştirmek ve derlemek isterseniz:

1. Bu depoyu klonlayın:
   ```bash
   git clone [https://github.com/06eren/Local-Network-Messenger.git](https://github.com/06eren/Local-Network-Messenger.git)
