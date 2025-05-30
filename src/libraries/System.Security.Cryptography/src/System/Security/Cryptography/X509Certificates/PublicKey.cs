// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Formats.Asn1;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.Asn1;
using Internal.Cryptography;

namespace System.Security.Cryptography.X509Certificates
{
    public sealed class PublicKey
    {
        private readonly Oid _oid;
        private AsymmetricAlgorithm? _key;

        public PublicKey(Oid oid, AsnEncodedData? parameters, AsnEncodedData keyValue)
            : this(oid, parameters, keyValue, skipCopy: false)
        {
        }

        internal PublicKey(Oid oid, AsnEncodedData? parameters, AsnEncodedData keyValue, bool skipCopy)
        {
            _oid = oid;

            if (skipCopy)
            {
                EncodedParameters = parameters;
                EncodedKeyValue = keyValue;
            }
            else
            {
                EncodedParameters = parameters is null ? null : new AsnEncodedData(parameters);
                EncodedKeyValue = new AsnEncodedData(keyValue);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PublicKey" /> class
        /// using SubjectPublicKeyInfo from an <see cref="AsymmetricAlgorithm" />.
        /// </summary>
        /// <param name="key">
        /// An asymmetric algorithm to obtain the SubjectPublicKeyInfo from.
        /// </param>
        /// <exception cref="CryptographicException">
        /// The SubjectPublicKeyInfo could not be decoded. The
        /// <see cref="AsymmetricAlgorithm.ExportSubjectPublicKeyInfo" /> must return a
        /// valid ASN.1-DER encoded X.509 SubjectPublicKeyInfo.
        /// </exception>
        /// <exception cref="NotImplementedException">
        /// <see cref="AsymmetricAlgorithm.ExportSubjectPublicKeyInfo" /> has not been overridden
        /// in a derived class.
        /// </exception>
        public PublicKey(AsymmetricAlgorithm key) : this(key.ExportSubjectPublicKeyInfo())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PublicKey" /> class
        /// using SubjectPublicKeyInfo from an <see cref="MLKem" />.
        /// </summary>
        /// <param name="key">
        /// An <see cref="MLKem" /> key to obtain the SubjectPublicKeyInfo from.
        /// </param>
        /// <exception cref="CryptographicException">
        /// The SubjectPublicKeyInfo could not be decoded. The
        /// <see cref="MLKem.ExportSubjectPublicKeyInfo" /> must return a
        /// valid ASN.1-DER encoded X.509 SubjectPublicKeyInfo.
        /// </exception>
        [Experimental(Experimentals.PostQuantumCryptographyDiagId, UrlFormat = Experimentals.SharedUrlFormat)]
        public PublicKey(MLKem key) : this(key.ExportSubjectPublicKeyInfo())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PublicKey" /> class
        /// using SubjectPublicKeyInfo from an <see cref="SlhDsa" />.
        /// </summary>
        /// <param name="key">
        /// An <see cref="SlhDsa" /> key to obtain the SubjectPublicKeyInfo from.
        /// </param>
        /// <exception cref="CryptographicException">
        /// The SubjectPublicKeyInfo could not be decoded. The
        /// <see cref="SlhDsa.ExportSubjectPublicKeyInfo" /> must return a
        /// valid ASN.1-DER encoded X.509 SubjectPublicKeyInfo.
        /// </exception>
        [Experimental(Experimentals.PostQuantumCryptographyDiagId, UrlFormat = Experimentals.SharedUrlFormat)]
        public PublicKey(SlhDsa key) : this(key.ExportSubjectPublicKeyInfo())
        {
        }

        private PublicKey(byte[] subjectPublicKeyInfo)
        {
            DecodeSubjectPublicKeyInfo(
                subjectPublicKeyInfo,
                out Oid localOid,
                out AsnEncodedData? localParameters,
                out AsnEncodedData localKeyValue);

            _oid = localOid;
            EncodedParameters = localParameters;
            EncodedKeyValue = localKeyValue;

            // Do not assign _key = key. Otherwise, the public Key property
            // will start returning non Rsa / Dsa types.
        }

        public AsnEncodedData EncodedKeyValue { get; }

        public AsnEncodedData? EncodedParameters { get; }

        [Obsolete(Obsoletions.PublicKeyPropertyMessage, DiagnosticId = Obsoletions.PublicKeyPropertyDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public AsymmetricAlgorithm Key
        {
            get
            {
                if (_key == null)
                {
                    switch (_oid.Value)
                    {
                        case Oids.Rsa:
                        case Oids.Dsa:
                            _key = X509Pal.Instance.DecodePublicKey(_oid, EncodedKeyValue.RawData, EncodedParameters?.RawData, null);
                            break;

                        default:
                            // This includes ECDSA, because an Oids.EcPublicKey key can be
                            // many different algorithm kinds, not necessarily with mutual exclusion.
                            //
                            // Plus, .NET Framework only supports RSA and DSA in this property.
                            throw new NotSupportedException(SR.NotSupported_KeyAlgorithm);
                    }
                }

                return _key;
            }
        }

        public Oid Oid => _oid;

        /// <summary>
        /// Attempts to export the current key in the X.509 SubjectPublicKeyInfo format into a provided buffer.
        /// </summary>
        /// <param name="destination">
        /// The byte span to receive the X.509 SubjectPublicKeyInfo data.
        /// </param>
        /// <param name="bytesWritten">
        /// When this method returns, contains a value that indicates the number of bytes written to
        /// <paramref name="destination" />. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="destination"/> is big enough to receive the output;
        /// otherwise, <see langword="false"/>.
        /// </returns>
        public bool TryExportSubjectPublicKeyInfo(Span<byte> destination, out int bytesWritten) =>
            EncodeSubjectPublicKeyInfo().TryEncode(destination, out bytesWritten);

        /// <summary>
        /// Exports the current key in the X.509 SubjectPublicKeyInfo format.
        /// </summary>
        /// <returns>
        /// A byte array containing the X.509 SubjectPublicKeyInfo representation of this key.
        /// </returns>
        public byte[] ExportSubjectPublicKeyInfo() =>
            EncodeSubjectPublicKeyInfo().Encode();

        /// <summary>
        /// Creates a new instance of <see cref="PublicKey" /> from a X.509 SubjectPublicKeyInfo.
        /// </summary>
        /// <param name="source">
        /// The bytes of an X.509 SubjectPublicKeyInfo structure in the ASN.1-DER encoding.
        /// </param>
        /// <param name="bytesRead">
        /// When this method returns, contains a value that indicates the number of bytes read from
        /// <paramref name="source" />. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>A public key representing the SubjectPublicKeyInfo.</returns>
        /// <exception cref="CryptographicException">
        /// The SubjectPublicKeyInfo could not be decoded.
        /// </exception>
        public static PublicKey CreateFromSubjectPublicKeyInfo(ReadOnlySpan<byte> source, out int bytesRead)
        {
            int read = DecodeSubjectPublicKeyInfo(
                source,
                out Oid localOid,
                out AsnEncodedData? localParameters,
                out AsnEncodedData localKeyValue);

            bytesRead = read;
            return new PublicKey(localOid, localParameters, localKeyValue, skipCopy: true);
        }

        /// <summary>
        /// Gets the <see cref="RSA" /> public key, or <see langword="null" /> if the key is not an RSA key.
        /// </summary>
        /// <returns>
        /// The public key, or <see langword="null" /> if the key is not an RSA key.
        /// </returns>
        /// <exception cref="CryptographicException">
        /// The key contents are corrupt or could not be read successfully.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        public RSA? GetRSAPublicKey()
        {
            if (_oid.Value != Oids.Rsa)
                return null;

            RSA rsa = RSA.Create();

            try
            {
                rsa.ImportSubjectPublicKeyInfo(ExportSubjectPublicKeyInfo(), out _);
                return rsa;
            }
            catch
            {
                rsa.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the <see cref="DSA" /> public key, or <see langword="null" /> if the key is not an DSA key.
        /// </summary>
        /// <returns>
        /// The public key, or <see langword="null" /> if the key is not an DSA key.
        /// </returns>
        /// <exception cref="CryptographicException">
        /// The key contents are corrupt or could not be read successfully.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        [UnsupportedOSPlatform("ios")]
        [UnsupportedOSPlatform("tvos")]
        public DSA? GetDSAPublicKey()
        {
            if (_oid.Value != Oids.Dsa)
                return null;

            DSA dsa = DSA.Create();

            try
            {
                dsa.ImportSubjectPublicKeyInfo(ExportSubjectPublicKeyInfo(), out _);
                return dsa;
            }
            catch
            {
                dsa.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the <see cref="ECDsa" /> public key, or <see langword="null" /> if the key is not an ECDsa key.
        /// </summary>
        /// <returns>
        /// The public key, or <see langword="null" /> if the key is not an ECDsa key.
        /// </returns>
        /// <exception cref="CryptographicException">
        /// The key contents are corrupt or could not be read successfully.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        public ECDsa? GetECDsaPublicKey()
        {
            if (_oid.Value != Oids.EcPublicKey)
                return null;

            ECDsa ecdsa = ECDsa.Create();

            try
            {
                ecdsa.ImportSubjectPublicKeyInfo(ExportSubjectPublicKeyInfo(), out _);
                return ecdsa;
            }
            catch
            {
                ecdsa.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the <see cref="ECDiffieHellman" /> public key, or <see langword="null" />
        /// if the key is not an ECDiffieHellman key.
        /// </summary>
        /// <returns>
        /// The public key, or <see langword="null" /> if the key is not an ECDiffieHellman key.
        /// </returns>
        /// <exception cref="CryptographicException">
        /// The key contents are corrupt or could not be read successfully.
        /// </exception>
        [UnsupportedOSPlatform("browser")]
        public ECDiffieHellman? GetECDiffieHellmanPublicKey()
        {
            if (_oid.Value != Oids.EcPublicKey)
                return null;

            ECDiffieHellman ecdh = ECDiffieHellman.Create();

            try
            {
                ecdh.ImportSubjectPublicKeyInfo(ExportSubjectPublicKeyInfo(), out _);
                return ecdh;
            }
            catch
            {
                ecdh.Dispose();
                throw;
            }
        }

        /// <summary>
        ///   Gets the <see cref="MLKem" /> public key, or <see langword="null" />
        ///   if the key is not an ML-KEM key.
        /// </summary>
        /// <returns>
        ///   The public key, or <see langword="null" /> if the key is not an ML-KEM key.
        /// </returns>
        /// <exception cref="PlatformNotSupportedException">
        ///   The object represents an ML-KEM public key, but the platform does not support the algorithm.
        /// </exception>
        /// <exception cref="CryptographicException">
        ///   The key contents are corrupt or could not be read successfully.
        /// </exception>
        [Experimental(Experimentals.PostQuantumCryptographyDiagId, UrlFormat = Experimentals.SharedUrlFormat)]
        [UnsupportedOSPlatform("browser")]
        public MLKem? GetMLKemPublicKey()
        {
            if (MLKemAlgorithm.FromOid(_oid.Value) is null)
                return null;

            return EncodeSubjectPublicKeyInfo().Encode(MLKem.ImportSubjectPublicKeyInfo);
        }

        /// <summary>
        ///   Gets the <see cref="MLDsa"/> public key, or <see langword="null" />
        ///   if the key is not an ML-DSA key.
        /// </summary>
        /// <returns>
        ///   The public key, or <see langword="null"/> if the key is not an ML-DSA key.
        /// </returns>
        /// <exception cref="PlatformNotSupportedException">
        ///   The object represents an ML-DSA public key, but the platform does not support the algorithm.
        /// </exception>
        /// <exception cref="CryptographicException">
        ///   The key contents are corrupt or could not be read successfully.
        /// </exception>
        [Experimental(Experimentals.PostQuantumCryptographyDiagId, UrlFormat = Experimentals.SharedUrlFormat)]
        [UnsupportedOSPlatform("browser")]
        public MLDsa? GetMLDsaPublicKey()
        {
            if (MLDsaAlgorithm.GetMLDsaAlgorithmFromOid(_oid.Value) is null)
                return null;

            return EncodeSubjectPublicKeyInfo().Encode(MLDsa.ImportSubjectPublicKeyInfo);
        }

        /// <summary>
        ///   Gets the <see cref="SlhDsa"/> public key, or <see langword="null" />
        ///   if the key is not an SLH-DSA key.
        /// </summary>
        /// <returns>
        ///   The public key, or <see langword="null"/> if the key is not an SLH-DSA key.
        /// </returns>
        /// <exception cref="PlatformNotSupportedException">
        ///   The object represents an SLH-DSA public key, but the platform does not support the algorithm.
        /// </exception>
        /// <exception cref="CryptographicException">
        ///   The key contents are corrupt or could not be read successfully.
        /// </exception>
        [Experimental(Experimentals.PostQuantumCryptographyDiagId, UrlFormat = Experimentals.SharedUrlFormat)]
        [UnsupportedOSPlatform("browser")]
        public SlhDsa? GetSlhDsaPublicKey() =>
            Helpers.IsSlhDsaOid(_oid.Value)
                ? EncodeSubjectPublicKeyInfo().Encode(SlhDsa.ImportSubjectPublicKeyInfo)
                : null;

        internal AsnWriter EncodeSubjectPublicKeyInfo()
        {
            SubjectPublicKeyInfoAsn spki = new SubjectPublicKeyInfoAsn
            {
                Algorithm = new AlgorithmIdentifierAsn
                {
                    Algorithm = _oid.Value ?? string.Empty,
                    Parameters = EncodedParameters?.RawData.ToNullableMemory(),
                },
                SubjectPublicKey = EncodedKeyValue.RawData,
            };

            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            spki.Encode(writer);
            return writer;
        }

        private static unsafe int DecodeSubjectPublicKeyInfo(
            ReadOnlySpan<byte> source,
            out Oid oid,
            out AsnEncodedData? parameters,
            out AsnEncodedData keyValue)
        {
            fixed (byte* ptr = &MemoryMarshal.GetReference(source))
            using (MemoryManager<byte> manager = new PointerMemoryManager<byte>(ptr, source.Length))
            {
                AsnValueReader reader = new AsnValueReader(source, AsnEncodingRules.DER);

                int read;
                SubjectPublicKeyInfoAsn spki;

                try
                {
                    read = reader.PeekEncodedValue().Length;
                    SubjectPublicKeyInfoAsn.Decode(ref reader, manager.Memory, out spki);
                }
                catch (AsnContentException e)
                {
                    throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding, e);
                }

                DecodeSubjectPublicKeyInfo(ref spki, out oid, out parameters, out keyValue);
                return read;
            }
        }

        internal static PublicKey DecodeSubjectPublicKeyInfo(ref SubjectPublicKeyInfoAsn spki)
        {
            DecodeSubjectPublicKeyInfo(
                ref spki,
                out Oid oid,
                out AsnEncodedData? parameters,
                out AsnEncodedData keyValue);

            return new PublicKey(oid, parameters, keyValue, skipCopy: true);
        }

        private static void DecodeSubjectPublicKeyInfo(
            ref SubjectPublicKeyInfoAsn spki,
            out Oid oid,
            out AsnEncodedData? parameters,
            out AsnEncodedData keyValue)
        {
            oid = new Oid(spki.Algorithm.Algorithm, null);
            keyValue = new AsnEncodedData(spki.SubjectPublicKey.Span);
            parameters = spki.Algorithm.Parameters switch
            {
                ReadOnlyMemory<byte> algParameters => new AsnEncodedData(algParameters.Span),
                _ => null,
            };
        }
    }
}
