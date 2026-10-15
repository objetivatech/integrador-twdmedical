# Conectividade por marca de equipamento

Levantamento (out/2026) de como os exames saem dos equipamentos das marcas atendidas e como o
integrador deve capturá-los. Baseado em manuais e páginas dos fabricantes e revendas: **cada
item precisa ser confirmado com um arquivo real exportado na clínica** (ver checklist no final).

## Resumo

| Marca | Equipamentos típicos | Como o exame sai | Formatos | Estratégia no integrador | Confiança |
|---|---|---|---|---|---|
| **Micromed** | Wincardio USB / AIR (ECG ligado ao PC), ErgoPC | Software no PC grava em pasta; envio DICOM opcional | PDF, JPG, DICOM (DCM com PDF/JPG) | **Pasta monitorada** (já implementado) + parser de PDF. Opcional: receptor DICOM | Alta |
| **Zoncare** | iMAC 120 / iMAC 10 (eletrocardiógrafo autônomo, com tela e impressora) | O próprio aparelho envia pela rede (Wi‑Fi/cabo) ou por pendrive | PDF, XML, DICOM, HL7, SCP, JPG, PNG, GDT | **Aparelho → pasta compartilhada (Samba) ou FTP no PC do integrador** + parser PDF/XML. Alternativa: receptor DICOM | Média |
| **Lepu Medical** | NeoECG S120/T180, linha ECG‑1100 (autônomos) | Aparelho envia por Wi‑Fi/rede (FTP, DICOM, HL7) ou pendrive | PDF, BMP, DICOM, HL7 aECG, SCP, XML | Igual ao Zoncare: **FTP/pasta compartilhada** ou **DICOM** | Média |
| **Contec** | ECG300G / ECG1200G (autônomos), Holter TLC5000, MAPA ABPM50 | ECGs: pendrive ou cabo para software no PC. Holter/MAPA: software no PC que **imprime** o laudo | Proprietário (.ecg), CSV; PDF só confirmado em revenda | **Impressora PDF virtual** apontando para a pasta monitorada; pendrive como alternativa | Baixa |
| **Cardioline / Cardios** | ECG100+/200+, touchECG, HD+ (Bluetooth), Holter e MAPA Cardios (CardioSmart, MAPAs, Dyna‑MAPA) | ECG: aparelho ou software envia por rede/USB. Holter/MAPA: software Cardios no PC | ECG: SCP (padrão), PDF/XML/GDT (pacote de conectividade), DICOM e HL7 (opcionais). Holter/MAPA: laudo impresso/exportado | ECG: **pasta (PDF+XML)** ou **DICOM**. Holter/MAPA: **pasta ou impressora PDF virtual** | Média |

Observações:
- A Cardioline **adquiriu a Cardios** (abr/2026). Por isso aparecem juntas: a Cardios vende os ECGs Cardioline no Brasil e mantém seus Holter/MAPA.
- Micromed (Coreum), Lepu (AI‑ECG/Carewell) e Cardios (CardioNet) têm **plataformas próprias de telemedicina**, que concorrem com a TWD Core. O integrador não deve depender delas.
- O PDF Zoncare de exemplo chegou com o nome `TWDZoncare3_<ID>_<nome>_null_..._<hora>.pdf`. Esse padrão sugere que algum aparelho Zoncare **já envia direto pela rede** para o sistema atual da TWD, por FTP ou HTTP. Vale descobrir como está configurado: dá para reaproveitar.

## Os quatro jeitos de um exame chegar ao integrador

```
1. Software no PC grava PDF numa pasta ............ Micromed, Cardios, Cardioline (cube/touchECG)
2. Aparelho autônomo envia pela rede .............. Zoncare, Lepu, Cardioline ECG100+/200+, (Contec?)
     a) para uma pasta compartilhada do Windows (Samba)  → cai no caso 1
     b) por FTP                                          → receptor FTP no integrador
     c) por DICOM (C-STORE)                              → receptor DICOM no integrador
3. Software que só imprime ........................ Contec Holter/MAPA, softwares antigos
     → impressora PDF virtual que salva sozinha na pasta monitorada → cai no caso 1
4. Pendrive ....................................... qualquer aparelho autônomo, como contingência
     → o integrador detecta o pendrive e importa a pasta de exames
```

## Mudanças propostas na arquitetura

1. **Separar "entrada" de "interpretação".**
   - *Entrada:* como o arquivo chega. Opções: pasta local, pasta de rede, receptor FTP, receptor DICOM, pendrive.
   - *Interpretador:* como ler os dados. Opções: texto do PDF (por layout de marca), XML do fabricante, HL7 aECG, tags DICOM, nome do arquivo.

   Hoje as duas coisas estão juntas na "fonte". Com a separação, cada nova marca é só um interpretador.
2. **Preferir dados estruturados.** Quando o aparelho manda XML ou DICOM junto com o PDF, os dados do paciente vêm de lá (nome, nascimento, sexo, ID, data), sem depender da leitura do texto. O PDF continua sendo o documento enviado para a plataforma.
3. **Um token por equipamento, não por computador.** Aparelhos autônomos (Zoncare, Lepu, Cardioline) mandam para um PC que pode receber de vários equipamentos ao mesmo tempo. Cada fonte (equipamento) passa a ter o próprio token, para a plataforma saber exatamente de qual aparelho veio o exame.
4. **Receptor DICOM embutido** (biblioteca fo‑dicom, compatível com .NET Framework). É o padrão hospitalar e funciona com Micromed, Zoncare, Lepu e Cardioline. É o canal "universal" mais robusto.

## Esforço estimado (após receber arquivos de exemplo)

| Item | Estimativa |
|---|---|
| Separar entrada/interpretador + token por equipamento | 2 dias |
| Interpretador por layout de PDF (cada marca/modelo) | 1–2 dias cada |
| Interpretador XML / HL7 aECG | 1–2 dias por formato |
| Receptor DICOM (recebe DCM, extrai PDF e dados do paciente) | 3 dias |
| Receptor FTP embutido | 2 dias |
| Importação por pendrive | 1 dia |
| Impressora PDF virtual | Usar uma pronta com salvamento automático (ex.: PDF24, PDFCreator), configurada pelo técnico, em vez de desenvolver um driver (driver próprio exige assinatura de código da Microsoft) |

## Checklist para cada marca/modelo (pedir ao técnico ou ao fabricante)

1. Modelo exato do aparelho e versão do software.
2. **Um exame de teste exportado**, com paciente fictício e todos os campos preenchidos, em **todos os formatos que o aparelho oferecer** (PDF, XML, DCM…).
3. Prints da tela de configuração de exportação/rede do aparelho: FTP, pasta de rede, DICOM, HL7.
4. Se o aparelho é autônomo, se está ligado à rede da clínica e se existe um PC próximo que fica ligado.
5. Para Holter/MAPA: um laudo final exportado do software de análise.

## Fontes

- Micromed: [Manual Wincardio USB](http://micromed.ind.br/micromedstore/Manual-Wincardio-USB.pdf), [Manual Wincardio AIR](http://micromed.ind.br/micromedstore/Manual-MEBT-100-Wincardio-Air.pdf), [Instruções DICOM para TI](http://micromed.ind.br/micromedstore/20210405-Instrucoes-DICOM-para-TI.pdf), [Histórico Wincardio (Coreum)](https://docs.coreum.health/docs/wincardio)
- Zoncare: [iMAC 120 (fabricante)](https://en.zoncare.com/research_detail/7.html), [Manual iMAC 120](https://www.manualslib.com/manual/3493043/Zoncare-Imac-120.html), [Zoncare Global](https://www.zoncareglobal.com/showroom/12-channel-digital-electrocardiograph-ecg-machine.html)
- Lepu: [NeoECG S120](https://en.lepumedical.com/products/neo-ecg-s120/), [Serviços de integração](https://en.lepumedical.com/lepu-medical-offers-advanced-medical-devices-and-integration-services.html), [AI‑ECG Platform](https://www.medicalexpo.com/prod/lepu-medical/product-95737-987544.html)
- Contec: [Manual ECG1200G](https://www.gimaitaly.com/DocumentiGIMA/Manuali/EN/M33224EN.pdf), [Manual ECG300G](https://www.gimaitaly.com/DocumentiGIMA/Manuali/EN/M33221EN.pdf), [ABPM50](https://www.amperordirect.com/pc/r-product-support/z-abpm50-reference.html)
- Cardioline/Cardios: [ECG200+](https://cardioline.com/products/ecg200/), [touchECG Digital Premium](https://cardioline.com/en/prodotto/touchecg-system-digital-premium/), [ECGWebApp](https://pdf.medicalexpo.com/pdf/cardioline/ecgwebapp/67948-152999.html), [Aquisição da Cardios](https://www.indianpharmapost.com/medical-device/lite/cardioline-acquires-cardios-to-expand-global-telecardiology-footprint-20002), [Software MAPAs](https://cardios.com.br/produtos/software-mapas), [CardioMapa](https://cardios.com.br/produtos/cardio-mapa)
