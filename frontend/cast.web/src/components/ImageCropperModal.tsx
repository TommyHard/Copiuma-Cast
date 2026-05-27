import { useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import Cropper, { Area } from 'react-easy-crop';

interface ImageCropperModalProps {
    isOpen: boolean;
    imageSrc: string;
    aspectRatio: number;
    onClose: () => void;
    onCropComplete: (croppedBlob: Blob) => void;
}

async function getCroppedImg(imageSrc: string, pixelCrop: Area): Promise<Blob> {
    const image = new Image();
    image.src = imageSrc;

    await new Promise((resolve, reject) => {
        image.onload = resolve;
        image.onerror = reject;
    });

    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('No 2d context');

    canvas.width = pixelCrop.width;
    canvas.height = pixelCrop.height;

    ctx.drawImage(
        image,
        pixelCrop.x,
        pixelCrop.y,
        pixelCrop.width,
        pixelCrop.height,
        0,
        0,
        pixelCrop.width,
        pixelCrop.height
    );

    return new Promise((resolve, reject) => {
        canvas.toBlob((blob) => {
            if (blob) resolve(blob);
            else reject(new Error('Canvas is empty'));
        }, 'image/jpeg', 0.95);
    });
}

export function ImageCropperModal({ isOpen, imageSrc, aspectRatio, onClose, onCropComplete }: ImageCropperModalProps) {
    const { t } = useTranslation();
    const [crop, setCrop] = useState({ x: 0, y: 0 });
    const [zoom, setZoom] = useState(1);
    const [croppedAreaPixels, setCroppedAreaPixels] = useState<Area | null>(null);

    const onCropCompleteHandler = useCallback((_croppedArea: Area, croppedAreaPixels: Area) => {
        setCroppedAreaPixels(croppedAreaPixels);
    }, []);

    const handleSave = async () => {
        if (!croppedAreaPixels) return;
        try {
            const blob = await getCroppedImg(imageSrc, croppedAreaPixels);
            onCropComplete(blob);
        } catch (e) {
            console.error('Ошибка при обрезке картинки:', e);
        }
    };

    if (!isOpen) return null;

    return (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 p-4 backdrop-blur-sm">
            <div className="flex h-[85vh] w-full max-w-2xl flex-col rounded-lg border border-border bg-bg-elevated p-5 shadow-2xl">
                <h4 className="mb-3 text-lg font-semibold text-fg">Настройка отображения</h4>

                <div className="relative flex-1 overflow-hidden rounded-md bg-black">
                    <Cropper
                        image={imageSrc}
                        crop={crop}
                        zoom={zoom}
                        aspect={aspectRatio}
                        onCropChange={setCrop}
                        onZoomChange={setZoom}
                        onCropComplete={onCropCompleteHandler}
                    />
                </div>

                <div className="mt-4 space-y-1 px-2">
                    <label className="text-xs text-fg-muted">Масштаб</label>
                    <input
                        type="range" min="1" max="3" step="0.05"
                        value={zoom} onChange={(e) => setZoom(parseFloat(e.target.value))}
                        className="w-full accent-accent"
                    />
                </div>

                <div className="mt-6 flex shrink-0 justify-end gap-3">
                    <button type="button" onClick={onClose} className="rounded-md border border-border px-4 py-2 text-sm text-fg hover:bg-bg">
                        {t('common.cancel')}
                    </button>
                    <button type="button" onClick={handleSave} className="rounded-md bg-accent px-6 py-2 text-sm font-medium text-white hover:opacity-90">
                        {t('common.save')}
                    </button>
                </div>
            </div>
        </div>
    );
}